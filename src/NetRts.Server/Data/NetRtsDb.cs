using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NetRts.Protocol;

namespace NetRts.Server.Data;

public sealed class PlayerRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>SHA-256 of the API key, hex encoded. The key itself is never stored.</summary>
    public string ApiKeyHash { get; set; } = "";

    public bool IsHouseBot { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>The Discord-verified person who claimed this bot, if anyone has.</summary>
    public Guid? OwnerAccountId { get; set; }

    public AccountRecord? Owner { get; set; }
}

/// <summary>A person, signed in with Discord. Refreshed from Discord on every sign-in.</summary>
public sealed class AccountRecord
{
    public Guid Id { get; set; }

    /// <summary>Discord's user id (a snowflake).</summary>
    public string DiscordId { get; set; } = "";

    /// <summary>The unique Discord username, e.g. "badgerfan".</summary>
    public string Username { get; set; } = "";

    /// <summary>The Discord display name, if set.</summary>
    public string? GlobalName { get; set; }

    /// <summary>Nickname in the configured Discord server, if set.</summary>
    public string? ServerNickname { get; set; }

    /// <summary>Discord avatar hash, for https://cdn.discordapp.com/avatars/{DiscordId}/{AvatarHash}.png.</summary>
    public string? AvatarHash { get; set; }

    /// <summary>Whether they belonged to the configured Discord server when they last signed in.</summary>
    public bool InServer { get; set; }

    /// <summary>Whether they were an administrator of the configured Discord server when they last signed in.</summary>
    public bool IsAdmin { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime LastSignInAt { get; set; }

    /// <summary>The name shown beside their bots: server nickname, else Discord display name, else username.</summary>
    public string DisplayName => ServerNickname ?? GlobalName ?? Username;
}

/// <summary>
/// Seasons are labels: everything is recorded in the current season (""). At the end of a season, label
/// it with UPDATE player_stats/matches SET "Season" = '2026-27' WHERE "Season" = ''; its ladders stay
/// viewable with ?season=2026-27 and the new season starts from zero.
/// </summary>
public static class Seasons
{
    public const string Current = "";
}

/// <summary>A player's rating and record in one category of one season; each is its own leaderboard.</summary>
public sealed class PlayerStatsRecord
{
    public Guid PlayerId { get; set; }
    public MatchCategory Category { get; set; }

    /// <summary>"" for the current season; past seasons are labelled by hand (see docs/deploying.md).</summary>
    public string Season { get; set; } = Seasons.Current;
    public int Rating { get; set; } = Elo.InitialRating;
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }

    /// <summary>League points: score margins over the players below you in each match's top four.</summary>
    public int Points { get; set; }
}

public sealed class MatchRecord
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int Seed { get; set; }
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public int MaxTicks { get; set; }
    public int TickIntervalMs { get; set; }
    public int Ticks { get; set; }
    public string Reason { get; set; } = "";
    public Guid? WinnerId { get; set; }
    public bool Rated { get; set; }
    public MatchCategory Category { get; set; }

    /// <summary>"" for the current season; past seasons are labelled by hand.</summary>
    public string Season { get; set; } = Seasons.Current;

    /// <summary>Serialized MatchSummaryDto (players, settings, outcome).</summary>
    public string SummaryJson { get; set; } = "";

    /// <summary>Serialized MatchResultDto.</summary>
    public string ResultJson { get; set; } = "";

    /// <summary>Serialized ReplayDto.</summary>
    public string ReplayJson { get; set; } = "";
}

/// <summary>
/// The server's database. The app uses one of the provider-specific subclasses below, each with its own
/// migrations (Data/Migrations/Sqlite, Data/Migrations/Postgres), because generated migrations are
/// tied to one provider's column types.
/// </summary>
public abstract class NetRtsDb(DbContextOptions options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<PlayerRecord> Players => Set<PlayerRecord>();
    public DbSet<MatchRecord> Matches => Set<MatchRecord>();
    public DbSet<AccountRecord> Accounts => Set<AccountRecord>();
    public DbSet<PlayerStatsRecord> PlayerStats => Set<PlayerStatsRecord>();

    /// <summary>Keys for sign-in cookies, kept in the database so a restart or redeploy doesn't sign everyone out.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerRecord>(e =>
        {
            e.ToTable("players");
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(32);
            e.HasIndex(p => p.Name).IsUnique();
            e.Property(p => p.ApiKeyHash).HasMaxLength(64);
            e.HasIndex(p => p.ApiKeyHash).IsUnique();
            e.HasOne(p => p.Owner).WithMany().HasForeignKey(p => p.OwnerAccountId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AccountRecord>(e =>
        {
            e.ToTable("accounts");
            e.HasKey(a => a.Id);
            e.Property(a => a.DiscordId).HasMaxLength(32);
            e.HasIndex(a => a.DiscordId).IsUnique();
            e.Property(a => a.Username).HasMaxLength(64);
            e.Property(a => a.GlobalName).HasMaxLength(64);
            e.Property(a => a.ServerNickname).HasMaxLength(64);
            e.Property(a => a.AvatarHash).HasMaxLength(64);
            e.Ignore(a => a.DisplayName);
        });

        modelBuilder.Entity<PlayerStatsRecord>(e =>
        {
            e.ToTable("player_stats");
            e.HasKey(s => new { s.PlayerId, s.Category, s.Season });
            e.Property(s => s.Category).HasConversion<string>().HasMaxLength(16);
            e.Property(s => s.Season).HasMaxLength(32);
            e.HasOne<PlayerRecord>().WithMany().HasForeignKey(s => s.PlayerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.Category, s.Season, s.Rating });
            e.HasIndex(s => new { s.Category, s.Season, s.Points });
        });

        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        modelBuilder.Entity<MatchRecord>(e =>
        {
            e.ToTable("matches");
            e.HasKey(m => m.Id);
            e.Property(m => m.Reason).HasMaxLength(32);
            e.HasIndex(m => m.CompletedAt);
            e.Property(m => m.Category).HasConversion<string>().HasMaxLength(16).HasDefaultValue(MatchCategory.Practice);
            e.HasIndex(m => new { m.Category, m.CompletedAt });
            e.Property(m => m.Season).HasMaxLength(32).HasDefaultValue(Seasons.Current);
        });
    }
}

public sealed class SqliteNetRtsDb(DbContextOptions<SqliteNetRtsDb> options) : NetRtsDb(options);

public sealed class PostgresNetRtsDb(DbContextOptions<PostgresNetRtsDb> options) : NetRtsDb(options);

/// <summary>Lets <c>dotnet ef migrations add --context SqliteNetRtsDb</c> build the model without starting the server.</summary>
public sealed class SqliteNetRtsDbFactory : IDesignTimeDbContextFactory<SqliteNetRtsDb>
{
    public SqliteNetRtsDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteNetRtsDb>().UseSqlite("Data Source=design.db").Options);
}

/// <summary>Lets <c>dotnet ef migrations add --context PostgresNetRtsDb</c> build the model without a database.</summary>
public sealed class PostgresNetRtsDbFactory : IDesignTimeDbContextFactory<PostgresNetRtsDb>
{
    public PostgresNetRtsDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresNetRtsDb>().UseNpgsql("Host=localhost;Database=netrtsdb").Options);
}
