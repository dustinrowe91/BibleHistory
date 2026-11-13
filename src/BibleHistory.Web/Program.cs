using BibleHistory.Web;
using BibleHistory.Web.Admin;
using BibleHistory.Web.Ask;
using BibleHistory.Web.Auth;
using BibleHistory.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database and sign-in keys live outside the app folder so deploys never touch them (see AppPaths)
var dataDir = AppPaths.DataDirectory(builder.Configuration, builder.Environment);
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite($"Data Source={Path.Combine(dataDir, "biblehistory.db")}"));
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")))
    .SetApplicationName("BibleHistory");

builder.AddSiteAuth();
builder.Services.Configure<AskOptions>(builder.Configuration.GetSection("Ask"));
builder.Services.AddSingleton<IAskModel, ClaudeAskModel>();
builder.Services.AddHealthChecks();

// Behind Azure App Service (or any proxy) the original scheme and client address arrive in X-Forwarded-* headers
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

// Create or upgrade the database on start-up. Use SQLite's rollback journal, not WAL (EF Core's default for a new
// database): WAL needs shared memory, which Azure App Service's /home network share does not reliably provide.
// Creating the file here first, with the journal mode set, means EF Core finds an existing database and keeps it.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(db.Database.GetConnectionString()))
    {
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=DELETE;";
        cmd.ExecuteNonQuery();
    }
    db.Database.Migrate();
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h.XFrameOptions = "SAMEORIGIN";
    await next();
});

// The existing pages, served as they are. Pages, scripts and study files are revalidated on every visit (cheap with
// ETags) so an update shows at once; sw.js keeps its own offline copy.
var site = SiteFiles.CreateProvider(app.Environment, app.Configuration);
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = site });
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = site,
    ContentTypeProvider = SiteFiles.ContentTypes(),
    OnPrepareResponse = ctx =>
    {
        var icon = ctx.Context.Request.Path.StartsWithSegments("/icons");
        ctx.Context.Response.Headers.CacheControl = icon ? "public, max-age=604800" : "no-cache";
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapAskEndpoints();
app.MapAdminEndpoints();
app.MapHealthChecks("/healthz");

app.Run();

public partial class Program;
