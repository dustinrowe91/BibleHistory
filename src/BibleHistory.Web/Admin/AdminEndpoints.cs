using BibleHistory.Web.Ask;
using BibleHistory.Web.Auth;
using BibleHistory.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BibleHistory.Web.Admin;

public record UserUpdate(string? Status, int? DailyLimit, bool? ResetLimit);

/// <summary>For admins only: approve or ban people, set their limits, and see every question asked and its cost.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDb>();
            var u = await Users.CurrentAsync(ctx.HttpContext, db);
            if (u is null) return Results.Unauthorized();
            if (!u.IsAdmin || u.Status != UserStatus.Approved) return Results.StatusCode(StatusCodes.Status403Forbidden);
            ctx.HttpContext.Items["admin"] = u;
            return await next(ctx);
        });

        admin.MapGet("/summary", async (AppDb db) =>
        {
            var now = DateTime.UtcNow;
            var today = now.Date;
            var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            // Cost is stored exactly (as text in SQLite), so it is added up here rather than in SQL
            var monthAsks = await db.Asks.Where(a => a.CreatedUtc >= month).Select(a => new { a.CreatedUtc, a.CostUsd }).ToListAsync();
            var statuses = await db.Users.GroupBy(u => u.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            return Results.Ok(new
            {
                pending = statuses.FirstOrDefault(s => s.Key == UserStatus.Pending)?.Count ?? 0,
                approved = statuses.FirstOrDefault(s => s.Key == UserStatus.Approved)?.Count ?? 0,
                banned = statuses.FirstOrDefault(s => s.Key == UserStatus.Banned)?.Count ?? 0,
                asksToday = monthAsks.Count(a => a.CreatedUtc >= today),
                costToday = monthAsks.Where(a => a.CreatedUtc >= today).Sum(a => a.CostUsd),
                asksThisMonth = monthAsks.Count,
                costThisMonth = monthAsks.Sum(a => a.CostUsd),
            });
        });

        admin.MapGet("/users", async (AppDb db, IOptions<AskOptions> o) =>
        {
            var today = DateTime.UtcNow.Date;
            // Waiting for approval first, then approved, then banned; most recently seen first within each
            var users = (await db.Users.ToListAsync()).OrderBy(u => u.Status).ThenByDescending(u => u.LastSeenUtc).ToList();
            var asks = await db.Asks.Select(a => new { a.UserId, a.CreatedUtc, a.CostUsd, a.Error }).ToListAsync();
            var byUser = asks.ToLookup(a => a.UserId);
            return Results.Ok(users.Select(u => new
            {
                u.Id, u.Name, u.Email, u.PictureUrl, status = u.Status.ToString().ToLowerInvariant(), u.IsAdmin,
                dailyLimit = AskEndpoints.LimitFor(u, o.Value), customLimit = u.DailyLimit is not null,
                u.CreatedUtc, u.LastSeenUtc,
                asks = byUser[u.Id].Count(),
                asksToday = byUser[u.Id].Count(a => a.CreatedUtc >= today && (a.Error == null || a.Error == AskEndpoints.Cancelled)),
                cost = byUser[u.Id].Sum(a => a.CostUsd),
            }));
        });

        admin.MapPost("/users/{id:int}", async (int id, UserUpdate body, HttpContext ctx, AppDb db) =>
        {
            var me = (AppUser)ctx.Items["admin"]!;
            var u = await db.Users.FindAsync(id);
            if (u is null) return Results.NotFound();
            if (body.Status is not null)
            {
                if (!Enum.TryParse<UserStatus>(body.Status, ignoreCase: true, out var status))
                    return Results.Json(new { error = "Status must be pending, approved or banned." }, statusCode: 400);
                if (u.Id == me.Id && status != UserStatus.Approved)
                    return Results.Json(new { error = "You can't remove your own access." }, statusCode: 400);
                if (u.Status != status) { u.Status = status; u.StatusChangedUtc = DateTime.UtcNow; }
            }
            if (body.ResetLimit == true) u.DailyLimit = null;
            else if (body.DailyLimit is { } limit)
            {
                if (limit is < 0 or > 1000) return Results.Json(new { error = "The daily limit must be between 0 and 1000." }, statusCode: 400);
                u.DailyLimit = limit;
            }
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).AddEndpointFilter(JsonOnly.Filter);

        // Every question, newest first, 50 at a time (?before=<id> for the next page, ?userId= for one person)
        admin.MapGet("/asks", async (AppDb db, int? userId, long? before) =>
        {
            var q = db.Asks.AsQueryable();
            if (userId is { } uid) q = q.Where(a => a.UserId == uid);
            if (before is { } b) q = q.Where(a => a.Id < b);
            var items = await q.OrderByDescending(a => a.Id).Take(50).Select(a => new
            {
                a.Id, a.UserId, userName = a.User!.Name, userEmail = a.User.Email,
                a.Question, a.Answer, a.Model, a.StopReason, a.Error,
                a.InputTokens, a.OutputTokens, a.CacheReadTokens, a.CacheWriteTokens, a.CostUsd, a.DurationMs, a.CreatedUtc,
            }).ToListAsync();
            return Results.Ok(items);
        });
    }
}
