using Microsoft.EntityFrameworkCore;

namespace NetRts.Server.Data;

public sealed class PlayerRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>SHA-256 of the API key, hex encoded. The key itself is never stored.</summary>
    public string ApiKeyHash { get; set; } = "";

    public bool IsHouseBot { get; set; }
    public int Rating { get; set; } = Elo.InitialRating;
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public DateTime CreatedAt { get; set; }
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

    /// <summary>Serialized MatchSummaryDto (players, settings, outcome).</summary>
    public string SummaryJson { get; set; } = "";

    /// <summary>Serialized MatchResultDto.</summary>
    public string ResultJson { get; set; } = "";

    /// <summary>Serialized ReplayDto.</summary>
    public string ReplayJson { get; set; } = "";
}

public sealed class NetRtsDb(DbContextOptions<NetRtsDb> options) : DbContext(options)
{
    public DbSet<PlayerRecord> Players => Set<PlayerRecord>();
    public DbSet<MatchRecord> Matches => Set<MatchRecord>();

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
            e.HasIndex(p => p.Rating);
        });

        modelBuilder.Entity<MatchRecord>(e =>
        {
            e.ToTable("matches");
            e.HasKey(m => m.Id);
            e.Property(m => m.Reason).HasMaxLength(32);
            e.HasIndex(m => m.CompletedAt);
        });
    }
}
