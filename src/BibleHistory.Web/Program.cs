using BibleHistory.Web;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
// Behind Azure App Service (or any proxy) the original scheme and client address arrive in X-Forwarded-* headers
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

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

app.MapHealthChecks("/healthz");

app.Run();

public partial class Program;
