using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BibleHistory.Web.Ask;
using BibleHistory.Web.Auth;
using BibleHistory.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BibleHistory.Web.Tests;

/// <summary>
/// The app with its own throwaway database, Google sign-in switched on with dummy credentials, a fake Claude, and a
/// test sign-in: send X-Test-Uid: &lt;user id&gt; to be that user.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public const string OwnerEmail = "owner@example.com";
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "bh-tests-" + Guid.NewGuid().ToString("N"));
    public FakeAskModel Model { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Data:Directory", DataDir);
        builder.UseSetting("Admin:Emails", OwnerEmail);
        builder.UseSetting("Authentication:Google:ClientId", "test-client-id");
        builder.UseSetting("Authentication:Google:ClientSecret", "test-client-secret");
        builder.UseSetting("Ask:DailyLimit", "5");
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IAskModel>(Model);
            s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.SchemeName, _ => { });
            s.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = TestAuth.SchemeName);
        });
    }

    /// <summary>Signs someone in the way Google sign-in does, and returns their user.</summary>
    public async Task<AppUser> SignInAsync(string email, string? name = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        return await Users.SignedInAsync(db, config, "g-" + email, email, name ?? email.Split('@')[0], null);
    }

    public async Task SetAsync(int userId, Action<AppUser> change)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        change((await db.Users.FindAsync(userId))!);
        await db.SaveChangesAsync();
    }

    public async Task<T> DbAsync<T>(Func<AppDb, Task<T>> read)
    {
        using var scope = Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    public HttpClient ClientFor(AppUser? user)
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (user is not null) c.DefaultRequestHeaders.Add(TestAuth.Header, user.Id.ToString());
        return c;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDir, recursive: true); } catch (IOException) { }
    }
}

public sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
    : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-Uid";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var uid)) return Task.FromResult(AuthenticateResult.NoResult());
        var id = new ClaimsIdentity([new Claim(Users.IdClaim, uid.ToString())], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), SchemeName)));
    }
}

/// <summary>Stands in for Claude: streams a fixed answer, or fails when told to.</summary>
public sealed class FakeAskModel : IAskModel
{
    public bool Ready { get; set; } = true;
    public string[] Pieces { get; set; } = ["Grace is God's ", "undeserved favor (Ephesians 2:8-9)."];
    public string StopReason { get; set; } = "end_turn";
    public bool Fail { get; set; }
    public List<string> Questions { get; } = [];

    public async IAsyncEnumerable<AskChunk> AnswerAsync(string question, [EnumeratorCancellation] CancellationToken ct)
    {
        lock (Questions) Questions.Add(question);
        await Task.Yield();
        yield return new AskText(Pieces[0]);
        if (Fail) throw new InvalidOperationException("Simulated API failure");
        foreach (var p in Pieces.Skip(1)) yield return new AskText(p);
        yield return new AskDone("claude-opus-5-5", StopReason, 3000, 400, 0, 0);
    }
}
