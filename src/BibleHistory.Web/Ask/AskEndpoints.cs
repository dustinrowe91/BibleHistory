using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Anthropic.Exceptions;
using BibleHistory.Web.Auth;
using BibleHistory.Web.Data;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BibleHistory.Web.Ask;

public record AskRequest(string? Question);

public static class AskEndpoints
{
    /// <summary>A reader who closes the page mid-answer still used the question; only our own failures are not counted.</summary>
    public const string Cancelled = "Cancelled by the reader";

    // One answer at a time per person
    static readonly ConcurrentDictionary<int, byte> InFlight = new();

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static int LimitFor(AppUser u, AskOptions o) => u.DailyLimit ?? o.DailyLimit;

    public static Task<int> UsedTodayAsync(AppDb db, int userId)
    {
        var since = DateTime.UtcNow.Date;
        return db.Asks.CountAsync(a => a.UserId == userId && a.CreatedUtc >= since && (a.Error == null || a.Error == Cancelled));
    }

    public static void MapAskEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Who is signed in, and whether they can ask. The pages call this to decide what to show.
        api.MapGet("/me", async (HttpContext ctx, AppDb db, IOptions<AskOptions> o, IConfiguration config, IAskModel model) =>
        {
            var signInReady = AuthSetup.GoogleConfigured(config);
            var u = await Users.CurrentAsync(ctx, db);
            if (u is null) return Results.Ok(new { signedIn = false, signInReady, askReady = model.Ready });
            return Results.Ok(new
            {
                signedIn = true, signInReady, askReady = model.Ready,
                name = u.Name, email = u.Email, picture = u.PictureUrl,
                status = u.Status.ToString().ToLowerInvariant(), isAdmin = u.IsAdmin,
                dailyLimit = LimitFor(u, o.Value), usedToday = await UsedTodayAsync(db, u.Id),
                maxQuestionLength = o.Value.MaxQuestionLength,
            });
        });

        // The reader's own recent questions
        api.MapGet("/ask/history", async (HttpContext ctx, AppDb db) =>
        {
            var u = await Users.CurrentAsync(ctx, db);
            if (u is null) return Results.Unauthorized();
            var items = await db.Asks.Where(a => a.UserId == u.Id && a.Error == null)
                .OrderByDescending(a => a.Id).Take(30)
                .Select(a => new { a.Id, a.Question, a.Answer, a.StopReason, a.CreatedUtc }).ToListAsync();
            return Results.Ok(items);
        });

        // Ask a question. The answer streams back as newline-delimited JSON:
        //   {"type":"text","text":"..."} ... then {"type":"done","stop":"end_turn","remaining":17} or {"type":"error","message":"..."}
        api.MapPost("/ask", async (HttpContext ctx, AskRequest req, AppDb db, IAskModel model, IOptions<AskOptions> o, ILogger<AskRequest> log) =>
        {
            var opts = o.Value;
            var u = await Users.CurrentAsync(ctx, db);
            if (u is null) return Problem(401, "Sign in to ask a question.");
            if (u.Status == UserStatus.Pending) return Problem(403, "Your account is waiting for approval.");
            if (u.Status != UserStatus.Approved) return Problem(403, "Your account can't use Ask.");
            if (!model.Ready) return Problem(503, "Ask isn't set up on this server yet.");

            var question = (req.Question ?? "").Trim();
            if (question.Length < 3) return Problem(400, "Please type a question.");
            if (question.Length > opts.MaxQuestionLength) return Problem(400, $"Please keep your question under {opts.MaxQuestionLength} characters.");

            var limit = LimitFor(u, opts);
            var used = await UsedTodayAsync(db, u.Id);
            if (used >= limit) return Problem(429, $"You've asked {used} questions today, the daily limit. Ask again tomorrow.");
            if (!InFlight.TryAdd(u.Id, 0)) return Problem(429, "Please wait for your current answer to finish.");

            try
            {
                var res = ctx.Response;
                res.ContentType = "application/x-ndjson; charset=utf-8";
                res.Headers.CacheControl = "no-store";
                res.Headers["X-Accel-Buffering"] = "no";
                ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

                var record = new AskRecord { UserId = u.Id, Question = question, CreatedUtc = DateTime.UtcNow, Model = opts.Model };
                var answer = new System.Text.StringBuilder();
                var clock = Stopwatch.StartNew();
                var ct = ctx.RequestAborted;
                try
                {
                    await foreach (var chunk in model.AnswerAsync(question, ct))
                    {
                        if (chunk is AskText t)
                        {
                            answer.Append(t.Text);
                            await Write(res, new { type = "text", text = t.Text }, ct);
                        }
                        else if (chunk is AskDone d)
                        {
                            record.Model = d.Model;
                            record.StopReason = d.StopReason;
                            record.InputTokens = (int)d.InputTokens;
                            record.OutputTokens = (int)d.OutputTokens;
                            record.CacheReadTokens = (int)d.CacheReadTokens;
                            record.CacheWriteTokens = (int)d.CacheWriteTokens;
                            record.CostUsd = Pricing.Estimate(d.Model, d.InputTokens, d.OutputTokens, d.CacheReadTokens, d.CacheWriteTokens);
                        }
                    }
                    await Write(res, new { type = "done", stop = record.StopReason, remaining = Math.Max(0, limit - used - 1) }, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    record.Error = Cancelled;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Ask failed for user {UserId}", u.Id);
                    record.Error = Truncate(ex.GetType().Name + ": " + ex.Message, 500);
                    var message = ex is AnthropicRateLimitException or Anthropic5xxException
                        ? "Ask is busy right now. Please try again in a minute; this question didn't count against your limit."
                        : "Something went wrong getting the answer. Please try again; this question didn't count against your limit.";
                    if (!ct.IsCancellationRequested) await Write(res, new { type = "error", message }, CancellationToken.None);
                }
                record.Answer = answer.ToString();
                record.DurationMs = (int)clock.ElapsedMilliseconds;
                db.Asks.Add(record);
                await db.SaveChangesAsync(CancellationToken.None);
                return Results.Empty;
            }
            finally
            {
                InFlight.TryRemove(u.Id, out _);
            }
        }).AddEndpointFilter(JsonOnly.Filter);
    }

    static IResult Problem(int status, string detail) => Results.Json(new { error = detail }, statusCode: status);

    static async Task Write(HttpResponse res, object line, CancellationToken ct)
    {
        await res.WriteAsync(JsonSerializer.Serialize(line, Json) + "\n", ct);
        await res.Body.FlushAsync(ct);
    }

    static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}
