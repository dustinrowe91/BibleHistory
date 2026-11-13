using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BibleHistory.Web.Ask;
using BibleHistory.Web.Auth;
using BibleHistory.Web.Data;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BibleHistory.Web.Tests;

public class AskTests(AppFactory app) : IClassFixture<AppFactory>
{
    static string NewEmail() => "reader-" + Guid.NewGuid().ToString("N")[..8] + "@example.com";

    async Task<AppUser> ApprovedReaderAsync(int? limit = null)
    {
        var u = await app.SignInAsync(NewEmail());
        await app.SetAsync(u.Id, x => { x.Status = UserStatus.Approved; x.DailyLimit = limit; });
        return u;
    }

    static Task<HttpResponseMessage> AskAsync(HttpClient c, string question) =>
        c.PostAsJsonAsync("/api/ask", new { question });

    static List<JsonElement> Lines(string body) =>
        body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToList();

    static async Task<JsonElement> MeAsync(HttpClient c) => await c.GetFromJsonAsync<JsonElement>("/api/me");

    [Fact]
    public async Task Database_lives_in_the_data_directory()
    {
        await app.SignInAsync(NewEmail());
        Assert.True(File.Exists(Path.Combine(app.DataDir, "biblehistory.db")));
        Assert.True(Directory.Exists(Path.Combine(app.DataDir, "keys")));
        // Rollback journal, not WAL: WAL is unreliable on Azure's /home network share
        var mode = await app.DbAsync(async db =>
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            return (string)(await cmd.ExecuteScalarAsync())!;
        });
        Assert.Equal("delete", mode);
        Assert.False(File.Exists(Path.Combine(app.DataDir, "biblehistory.db-wal")));
    }

    [Fact]
    public async Task Signed_out_reader_sees_sign_in()
    {
        var me = await MeAsync(app.ClientFor(null));
        Assert.False(me.GetProperty("signedIn").GetBoolean());
        Assert.True(me.GetProperty("signInReady").GetBoolean());
    }

    [Fact]
    public async Task New_accounts_wait_for_approval()
    {
        var u = await app.SignInAsync(NewEmail(), "Ruth Moabite");
        Assert.Equal(UserStatus.Pending, u.Status);
        Assert.False(u.IsAdmin);
        var me = await MeAsync(app.ClientFor(u));
        Assert.Equal("pending", me.GetProperty("status").GetString());
        Assert.Equal("Ruth Moabite", me.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Owner_email_is_admin_and_approved()
    {
        var u = await app.SignInAsync(AppFactory.OwnerEmail);
        Assert.True(u.IsAdmin);
        Assert.Equal(UserStatus.Approved, u.Status);
    }

    [Fact]
    public async Task Signing_in_again_keeps_the_same_account_and_status()
    {
        var email = NewEmail();
        var first = await app.SignInAsync(email, "Old Name");
        await app.SetAsync(first.Id, x => x.Status = UserStatus.Approved);
        var again = await app.SignInAsync(email, "New Name");
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(UserStatus.Approved, again.Status);
        Assert.Equal("New Name", again.Name);
    }

    [Fact]
    public async Task Ask_needs_sign_in()
    {
        var res = await AskAsync(app.ClientFor(null), "Who was Melchizedek?");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData(UserStatus.Pending)]
    [InlineData(UserStatus.Banned)]
    public async Task Ask_needs_approval(UserStatus status)
    {
        var u = await app.SignInAsync(NewEmail());
        await app.SetAsync(u.Id, x => x.Status = status);
        var res = await AskAsync(app.ClientFor(u), "Who was Melchizedek?");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Approved_reader_gets_a_streamed_answer_that_is_logged()
    {
        var u = await ApprovedReaderAsync();
        var c = app.ClientFor(u);
        var res = await AskAsync(c, "  What is grace?  ");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/x-ndjson", res.Content.Headers.ContentType?.MediaType);

        var lines = Lines(await res.Content.ReadAsStringAsync());
        var text = string.Concat(lines.Where(l => l.GetProperty("type").GetString() == "text").Select(l => l.GetProperty("text").GetString()));
        Assert.Equal("Grace is God's undeserved favor (Ephesians 2:8-9).", text);
        var done = lines.Last();
        Assert.Equal("done", done.GetProperty("type").GetString());
        Assert.Equal("end_turn", done.GetProperty("stop").GetString());
        Assert.Equal(4, done.GetProperty("remaining").GetInt32());

        var rec = await app.DbAsync(db => db.Asks.SingleAsync(a => a.UserId == u.Id));
        Assert.Equal("What is grace?", rec.Question);
        Assert.Equal(text, rec.Answer);
        Assert.Null(rec.Error);
        Assert.Equal(400, rec.OutputTokens);
        Assert.Equal(Pricing.Estimate("claude-opus-5-5", 3000, 400, 0, 0), rec.CostUsd);
        Assert.Equal(0.020m, rec.CostUsd);

        var me = await MeAsync(c);
        Assert.Equal(1, me.GetProperty("usedToday").GetInt32());
        var history = await c.GetFromJsonAsync<JsonElement>("/api/ask/history");
        Assert.Equal("What is grace?", history[0].GetProperty("question").GetString());
    }

    [Fact]
    public async Task Daily_limit_is_enforced()
    {
        var u = await ApprovedReaderAsync(limit: 1);
        var c = app.ClientFor(u);
        Assert.Equal(HttpStatusCode.OK, (await AskAsync(c, "First question?")).StatusCode);
        var second = await AskAsync(c, "Second question?");
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Contains("daily limit", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Failed_answers_do_not_count_against_the_limit()
    {
        var u = await ApprovedReaderAsync();
        var c = app.ClientFor(u);
        var model = new FakeAskModel { Fail = true };
        using var failing = app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IAskModel>(model)));
        var fc = failing.CreateClient();
        fc.DefaultRequestHeaders.Add(TestAuth.Header, u.Id.ToString());
        var res = await AskAsync(fc, "Will this fail?");
        var lines = Lines(await res.Content.ReadAsStringAsync());
        Assert.Equal("error", lines.Last().GetProperty("type").GetString());
        Assert.Contains("didn't count", lines.Last().GetProperty("message").GetString());

        var rec = await app.DbAsync(db => db.Asks.SingleAsync(a => a.UserId == u.Id));
        Assert.StartsWith("InvalidOperationException", rec.Error);
        Assert.Equal("Grace is God's ", rec.Answer);
        Assert.Equal(0, (await MeAsync(c)).GetProperty("usedToday").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ?  ")]
    public async Task Empty_questions_are_rejected(string q)
    {
        var res = await AskAsync(app.ClientFor(await ApprovedReaderAsync()), q);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Long_questions_are_rejected()
    {
        var res = await AskAsync(app.ClientFor(await ApprovedReaderAsync()), new string('a', 1001));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Ask_only_accepts_json()
    {
        var c = app.ClientFor(await ApprovedReaderAsync());
        var res = await c.PostAsync("/api/ask", new FormUrlEncodedContent([new("question", "What is grace?")]));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, res.StatusCode);
    }

    [Fact]
    public async Task Sign_out_only_accepts_json()
    {
        var c = app.ClientFor(null);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsync("/signout", new StringContent(""))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/signout", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Fact]
    public async Task Sign_in_goes_to_Google_and_only_returns_to_this_site()
    {
        var c = app.ClientFor(null);
        var res = await c.GetAsync("/signin?returnUrl=%2Fkings.html");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.StartsWith("https://accounts.google.com/", res.Headers.Location!.ToString());

        Assert.True(AuthSetup.IsLocalUrl("/ask.html"));
        Assert.False(AuthSetup.IsLocalUrl("//evil.example/"));
        Assert.False(AuthSetup.IsLocalUrl("/\\evil.example/"));
        Assert.False(AuthSetup.IsLocalUrl("https://evil.example/"));
        Assert.False(AuthSetup.IsLocalUrl(null));
    }

    [Fact]
    public void Pricing_matches_published_rates()
    {
        Assert.Equal(0.004m + 0.020m, Pricing.Estimate("claude-opus-5-5", 1000, 1000, 0, 0));
        Assert.Equal(0.0002m + 0.005m, Pricing.Estimate("claude-opus-5-5", 0, 0, 1000, 1000));
        Assert.Equal(0.025m + 0.005m, Pricing.Estimate("claude-opus-4-8", 1000, 1000, 0, 0));
    }
}
