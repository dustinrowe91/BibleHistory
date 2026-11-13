using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BibleHistory.Web.Data;

namespace BibleHistory.Web.Tests;

public class AdminTests(AppFactory app) : IClassFixture<AppFactory>
{
    static string NewEmail() => "reader-" + Guid.NewGuid().ToString("N")[..8] + "@example.com";

    [Fact]
    public async Task Readers_cannot_use_admin()
    {
        var reader = await app.SignInAsync(NewEmail());
        await app.SetAsync(reader.Id, u => u.Status = UserStatus.Approved);
        var c = app.ClientFor(reader);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync($"/api/admin/users/{reader.Id}", new { dailyLimit = 999 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.ClientFor(null).GetAsync("/api/admin/asks")).StatusCode);
    }

    [Fact]
    public async Task Admin_approves_then_the_reader_can_ask()
    {
        var owner = await app.SignInAsync(AppFactory.OwnerEmail);
        var reader = await app.SignInAsync(NewEmail(), "Lydia");
        var admin = app.ClientFor(owner);

        var users = await admin.GetFromJsonAsync<JsonElement>("/api/admin/users");
        var row = users.EnumerateArray().Single(u => u.GetProperty("id").GetInt32() == reader.Id);
        Assert.Equal("pending", row.GetProperty("status").GetString());
        Assert.Equal("pending", users[0].GetProperty("status").GetString()); // people waiting are listed first

        var readerClient = app.ClientFor(reader);
        Assert.Equal(HttpStatusCode.Forbidden, (await readerClient.PostAsJsonAsync("/api/ask", new { question = "Who was Lydia?" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/users/{reader.Id}", new { status = "approved", dailyLimit = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await readerClient.PostAsJsonAsync("/api/ask", new { question = "Who was Lydia?" })).StatusCode);

        var asks = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/asks?userId={reader.Id}");
        Assert.Equal("Who was Lydia?", asks[0].GetProperty("question").GetString());
        Assert.Equal("Lydia", asks[0].GetProperty("userName").GetString());

        var summary = await admin.GetFromJsonAsync<JsonElement>("/api/admin/summary");
        Assert.True(summary.GetProperty("asksToday").GetInt32() >= 1);
        Assert.True(summary.GetProperty("costThisMonth").GetDecimal() > 0);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/users/{reader.Id}", new { status = "banned" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await readerClient.PostAsJsonAsync("/api/ask", new { question = "Who was Lydia?" })).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_lock_themselves_out()
    {
        var owner = await app.SignInAsync(AppFactory.OwnerEmail);
        var res = await app.ClientFor(owner).PostAsJsonAsync($"/api/admin/users/{owner.Id}", new { status = "banned" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Bad_updates_are_rejected()
    {
        var owner = await app.SignInAsync(AppFactory.OwnerEmail);
        var reader = await app.SignInAsync(NewEmail());
        var admin = app.ClientFor(owner);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/users/{reader.Id}", new { status = "king" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/users/{reader.Id}", new { dailyLimit = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/admin/users/999999", new { status = "approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await admin.PostAsync($"/api/admin/users/{reader.Id}", new FormUrlEncodedContent([new("status", "approved")]))).StatusCode);
    }
}
