using System.Security.Claims;
using BibleHistory.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BibleHistory.Web.Auth;

public static class Users
{
    /// <summary>The claim in the sign-in cookie that holds our own user id (AppUser.Id).</summary>
    public const string IdClaim = "bh:uid";

    /// <summary>
    /// Records a Google sign-in: creates the account the first time (Pending), or refreshes name, email and picture.
    /// Emails listed in Admin:Emails become admins and are approved automatically.
    /// </summary>
    public static async Task<AppUser> SignedInAsync(AppDb db, IConfiguration config, string googleId, string email, string name, string? picture, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var user = await db.Users.SingleOrDefaultAsync(u => u.GoogleId == googleId, ct);
        if (user is null)
        {
            user = new AppUser { GoogleId = googleId, Email = email, Name = name, CreatedUtc = now };
            db.Users.Add(user);
        }
        user.Email = email;
        user.Name = string.IsNullOrWhiteSpace(name) ? email : name;
        user.PictureUrl = picture;
        user.LastSeenUtc = now;
        if (IsAdminEmail(config, email))
        {
            user.IsAdmin = true;
            if (user.Status != UserStatus.Approved) { user.Status = UserStatus.Approved; user.StatusChangedUtc = now; }
        }
        await db.SaveChangesAsync(ct);
        return user;
    }

    static bool IsAdminEmail(IConfiguration config, string email)
    {
        // Admin:Emails is a comma- or semicolon-separated list, set in the host's settings (not in the repository)
        var list = config["Admin:Emails"] ?? "";
        return list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(e => string.Equals(e, email, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The signed-in user, read fresh from the database so approvals and bans apply at once. Null if signed out or unknown.</summary>
    public static async Task<AppUser?> CurrentAsync(HttpContext ctx, AppDb db)
    {
        var id = ctx.User.FindFirstValue(IdClaim);
        if (!int.TryParse(id, out var uid)) return null;
        return await db.Users.SingleOrDefaultAsync(u => u.Id == uid, ctx.RequestAborted);
    }
}
