using Microsoft.EntityFrameworkCore;

namespace BibleHistory.Web.Data;

public enum UserStatus { Pending, Approved, Banned }

/// <summary>Someone who has signed in with Google. New accounts start Pending; an admin approves them before they can use Ask.</summary>
public class AppUser
{
    public int Id { get; set; }
    /// <summary>Google's stable account id (the "sub" claim). Email can change; this cannot.</summary>
    public required string GoogleId { get; set; }
    public required string Email { get; set; }
    public required string Name { get; set; }
    public string? PictureUrl { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Pending;
    public bool IsAdmin { get; set; }
    /// <summary>Questions allowed per day (UTC). Null uses Ask:DailyLimit.</summary>
    public int? DailyLimit { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public DateTime? StatusChangedUtc { get; set; }
    public List<AskRecord> Asks { get; set; } = [];
}

/// <summary>One question asked through Ask, who asked it, the answer, and what it cost.</summary>
public class AskRecord
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public required string Question { get; set; }
    public string Answer { get; set; } = "";
    public string Model { get; set; } = "";
    public string? StopReason { get; set; }
    /// <summary>Set when the answer failed or was cut short; such questions do not count against the daily limit.</summary>
    public string? Error { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int CacheWriteTokens { get; set; }
    public decimal CostUsd { get; set; }
    public int DurationMs { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AskRecord> Asks => Set<AskRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.GoogleId).IsUnique();
            e.HasIndex(u => u.Email);
            e.Property(u => u.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(u => u.GoogleId).HasMaxLength(64);
            e.Property(u => u.Email).HasMaxLength(320);
            e.Property(u => u.Name).HasMaxLength(200);
            e.Property(u => u.PictureUrl).HasMaxLength(1000);
        });
        b.Entity<AskRecord>(e =>
        {
            e.HasIndex(a => new { a.UserId, a.CreatedUtc });
            e.HasIndex(a => a.CreatedUtc);
            e.HasOne(a => a.User).WithMany(u => u.Asks).HasForeignKey(a => a.UserId);
            e.Property(a => a.Question).HasMaxLength(2000);
            e.Property(a => a.Model).HasMaxLength(64);
            e.Property(a => a.StopReason).HasMaxLength(32);
            e.Property(a => a.Error).HasMaxLength(500);
            // SQLite has no decimal type; store cost as TEXT so it stays exact
            e.Property(a => a.CostUsd).HasConversion<string>();
        });
    }
}
