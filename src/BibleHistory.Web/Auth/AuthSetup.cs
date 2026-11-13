using System.Security.Claims;
using BibleHistory.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

namespace BibleHistory.Web.Auth;

/// <summary>
/// Sign-in with Google only. Google proves who the person is; a cookie keeps them signed in for 30 days; our own
/// Users table (see <see cref="Users"/>) records them and holds their approval status. Sign-in is switched on when
/// Authentication:Google:ClientId and ClientSecret are set; without them the site still runs and Ask says sign-in is
/// not set up.
/// </summary>
public static class AuthSetup
{
    public static bool GoogleConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["Authentication:Google:ClientId"]) &&
        !string.IsNullOrWhiteSpace(config["Authentication:Google:ClientSecret"]);

    public static void AddSiteAuth(this WebApplicationBuilder builder)
    {
        var auth = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "bh.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                // The site's pages are static: answer the API with a status code instead of redirecting to a login page
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            });

        if (GoogleConfigured(builder.Configuration))
        {
            auth.AddGoogle(o =>
            {
                o.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
                o.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
                o.ClaimActions.MapJsonKey("urn:google:picture", "picture");
                o.Events.OnCreatingTicket = async ctx =>
                {
                    var p = ctx.Principal!;
                    var googleId = p.FindFirstValue(ClaimTypes.NameIdentifier);
                    var email = p.FindFirstValue(ClaimTypes.Email);
                    if (string.IsNullOrEmpty(googleId) || string.IsNullOrEmpty(email))
                    {
                        ctx.Fail("Google did not return an account id and email.");
                        return;
                    }
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDb>();
                    var user = await Users.SignedInAsync(db, builder.Configuration, googleId, email,
                        p.FindFirstValue(ClaimTypes.Name) ?? email, p.FindFirstValue("urn:google:picture"), ctx.HttpContext.RequestAborted);
                    ctx.Identity!.AddClaim(new Claim(Users.IdClaim, user.Id.ToString()));
                };
            });
        }
        builder.Services.AddAuthorization();
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        // /signin?returnUrl=/ask.html sends the browser to Google and back
        app.MapGet("/signin", (HttpContext ctx, string? returnUrl, IConfiguration config) =>
        {
            if (!GoogleConfigured(config))
                return Results.Text("Sign-in is not set up on this server yet.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var target = IsLocalUrl(returnUrl) ? returnUrl! : "/ask.html";
            return Results.Challenge(new AuthenticationProperties { RedirectUri = target }, [GoogleDefaults.AuthenticationScheme]);
        });

        app.MapPost("/signout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AddEndpointFilter(JsonOnly.Filter);
    }

    /// <summary>Only same-site paths, never "//evil.example" or "/\evil.example"</summary>
    public static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}

/// <summary>
/// Every request that changes something must be a JSON POST. Browsers will not send a cross-site JSON POST without
/// asking the server first (CORS preflight, which this app never allows), and the sign-in cookie is SameSite=Lax,
/// so another site cannot make a signed-in reader ask questions, sign out, or approve anyone.
/// </summary>
public static class JsonOnly
{
    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next) =>
        ctx.HttpContext.Request.HasJsonContentType()
            ? await next(ctx)
            : Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
}
