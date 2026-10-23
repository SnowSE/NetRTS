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

/// <summary>Something a person has done for the first time, kept so it is only celebrated once.</summary>
public sealed class AccountMilestoneRecord
{
    public Guid AccountId { get; set; }
    public string Kind { get; set; } = "";
    public DateTime ReachedAt { get; set; }
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

    /// <summary>
    /// League points: score margins over the players below you in each match's top four. When a bot
    /// plays two or more heats of one league match, its lowest-scoring heat of that night doesn't count.
    /// </summary>
    public int Points { get; set; }

    /// <summary>OpenSkill (Plackett-Luce) skill estimate: the mean. See <see cref="OpenSkill"/>.</summary>
    public double Mu { get; set; } = OpenSkill.DefaultMu;

    /// <summary>
    /// OpenSkill uncertainty. 0 only on rows that predate skills, until <see cref="SkillBackfill"/> runs at startup.
    /// </summary>
    public double Sigma { get; set; } = OpenSkill.DefaultSigma;
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

    /// <summary>
    /// The <c>GameRules.Version</c> the match was played under; null for matches recorded before
    /// versions were kept.
    /// </summary>
    public string? RulesVersion { get; set; }
}

/// <summary>A finished match's recorded frames (see <c>ReplayFrameRecorder</c>): the manifest, with its chunks alongside.</summary>
public sealed class MatchFramesRecord
{
    public Guid MatchId { get; set; }
    public string RulesVersion { get; set; } = "";
    public int ChunkTicks { get; set; }
    public int ChunkCount { get; set; }
    public int LastTick { get; set; }

    /// <summary>Rebuilt from the replay because the match was played before frames were recorded.</summary>
    public bool Rebuilt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Brotli-compressed ReplayFramesDto JSON (map and play-by-play included), served as is.</summary>
    public byte[] Manifest { get; set; } = [];
}

/// <summary>One chunk of frames: Brotli-compressed, newline-separated SpectatorStateDto JSON.</summary>
public sealed class MatchFrameChunkRecord
{
    public Guid MatchId { get; set; }
    public int Index { get; set; }
    public byte[] Data { get; set; } = [];
}

public enum MatchResultKind
{
    Win,
    Loss,
    Draw,
}

/// <summary>
/// One registered bot's part in one finished match: an index over <c>matches</c> for match history,
/// achievements and the nightly recap. Second and later copies of a house bot have no row.
/// </summary>
public sealed class MatchPlayerRecord
{
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public int Slot { get; set; }
    public DateTime CompletedAt { get; set; }
    public MatchCategory Category { get; set; }
    public bool IsHouseBot { get; set; }

    /// <summary>The bot's owner when the match was played.</summary>
    public Guid? OwnerAccountId { get; set; }

    public MatchResultKind Result { get; set; }

    /// <summary>Finishing place: the winner, then everyone else by score; equal scores share a place.</summary>
    public int Place { get; set; }

    public int PlayerCount { get; set; }
    public int Ticks { get; set; }
    public MatchEndReason Reason { get; set; }
    public int Score { get; set; }
    public int Gathered { get; set; }
    public int UnitsKilled { get; set; }
    public int UnitsLost { get; set; }
    public int BuildingsDestroyed { get; set; }

    /// <summary>The bot surrendered.</summary>
    public bool Surrendered { get; set; }

    /// <summary>
    /// Opponents that are real bots: not house bots, and not owned by this bot's owner. Achievements
    /// other than the first steps need one, so they can't be farmed.
    /// </summary>
    public int RealOpponents { get; set; }

    /// <summary>Rating in the match's category before and after, when the match was rated.</summary>
    public int? RatingBefore { get; set; }

    public int? RatingAfter { get; set; }

    /// <summary>How the bot played, e.g. "archer-rush" (see <c>Strategies</c>); null when unknown.</summary>
    public string? Strategy { get; set; }

    /// <summary>
    /// League points this match earned (0 in practice). The season total leaves out each bot's lowest
    /// heat of any league match it played two or more heats of (see <see cref="LeaguePoints.Night"/>).
    /// </summary>
    public int Points { get; set; }

    /// <summary>For a heat: the league match it belongs to (the heat's parent match).</summary>
    public Guid? LeagueMatchId { get; set; }

    /// <summary>
    /// Whether <see cref="Points"/> is in the season total yet. A heat's points wait until its league
    /// match is closed (see <c>LeagueFinalizer</c>), when the match's total, less the lowest heat, is added.
    /// </summary>
    public bool PointsCounted { get; set; } = true;
}

/// <summary>An achievement a bot has earned (see <c>AchievementCatalog</c>).</summary>
public sealed class BotAchievementRecord
{
    public Guid PlayerId { get; set; }
    public string AchievementId { get; set; } = "";
    public DateTime EarnedAt { get; set; }

    /// <summary>The match that earned it, if one did.</summary>
    public Guid? MatchId { get; set; }
}

/// <summary>A ladder as it stood one night, for the recap's movers and the ladder achievements.</summary>
public sealed class LadderSnapshotRecord
{
    /// <summary>The night it was taken (local date, midnight UTC).</summary>
    public DateTime Date { get; set; }

    public MatchCategory Category { get; set; }
    public Guid PlayerId { get; set; }
    public int Rank { get; set; }
    public int Rating { get; set; }
    public int Points { get; set; }

    /// <summary>Displayed skill (<see cref="OpenSkill.Display"/>); 0 in snapshots taken before skills existed.</summary>
    public int Skill { get; set; }
}

/// <summary>One night's recap show.</summary>
public sealed class RecapEpisodeRecord
{
    public Guid Id { get; set; }

    /// <summary>The day the episode covers (local date, midnight UTC).</summary>
    public DateTime Date { get; set; }

    public string Title { get; set; } = "";

    /// <summary>A sentence or two for the episode list.</summary>
    public string Summary { get; set; } = "";

    /// <summary>The video: a blob name in the recaps container, or an absolute URL.</summary>
    public string Video { get; set; } = "";

    /// <summary>Its poster frame, the same way; empty if none.</summary>
    public string Thumbnail { get; set; } = "";

    public int DurationSeconds { get; set; }

    /// <summary>Serialized list of RecapLineDto: who said what, for the transcript.</summary>
    public string TranscriptJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }
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
    public DbSet<MatchFramesRecord> MatchFrames => Set<MatchFramesRecord>();
    public DbSet<MatchFrameChunkRecord> MatchFrameChunks => Set<MatchFrameChunkRecord>();
    public DbSet<AccountMilestoneRecord> AccountMilestones => Set<AccountMilestoneRecord>();
    public DbSet<MatchPlayerRecord> MatchPlayers => Set<MatchPlayerRecord>();
    public DbSet<BotAchievementRecord> BotAchievements => Set<BotAchievementRecord>();
    public DbSet<LadderSnapshotRecord> LadderSnapshots => Set<LadderSnapshotRecord>();
    public DbSet<RecapEpisodeRecord> RecapEpisodes => Set<RecapEpisodeRecord>();

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
            e.Property(m => m.RulesVersion).HasMaxLength(32);
        });

        modelBuilder.Entity<MatchFramesRecord>(e =>
        {
            e.ToTable("match_frames");
            e.HasKey(f => f.MatchId);
            e.Property(f => f.RulesVersion).HasMaxLength(32);
            e.HasOne<MatchRecord>().WithOne().HasForeignKey<MatchFramesRecord>(f => f.MatchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchFrameChunkRecord>(e =>
        {
            e.ToTable("match_frame_chunks");
            e.HasKey(c => new { c.MatchId, c.Index });
            e.HasOne<MatchFramesRecord>().WithMany().HasForeignKey(c => c.MatchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccountMilestoneRecord>(e =>
        {
            e.ToTable("account_milestones");
            e.HasKey(m => new { m.AccountId, m.Kind });
            e.Property(m => m.Kind).HasMaxLength(32);
            e.HasIndex(m => new { m.Kind, m.ReachedAt });
            e.HasOne<AccountRecord>().WithMany().HasForeignKey(m => m.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchPlayerRecord>(e =>
        {
            e.ToTable("match_players");
            e.HasKey(m => new { m.MatchId, m.PlayerId });
            e.Property(m => m.Category).HasConversion<string>().HasMaxLength(16);
            e.Property(m => m.Result).HasConversion<string>().HasMaxLength(8);
            e.Property(m => m.Reason).HasConversion<string>().HasMaxLength(16);
            e.Property(m => m.Strategy).HasMaxLength(32);
            e.HasIndex(m => new { m.PlayerId, m.CompletedAt });
            e.HasIndex(m => m.CompletedAt);
            e.HasIndex(m => new { m.LeagueMatchId, m.PlayerId });
            e.HasOne<MatchRecord>().WithMany().HasForeignKey(m => m.MatchId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PlayerRecord>().WithMany().HasForeignKey(m => m.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BotAchievementRecord>(e =>
        {
            e.ToTable("bot_achievements");
            e.HasKey(a => new { a.PlayerId, a.AchievementId });
            e.Property(a => a.AchievementId).HasMaxLength(40);
            e.HasIndex(a => new { a.AchievementId, a.EarnedAt });
            e.HasIndex(a => a.EarnedAt);
            e.HasOne<PlayerRecord>().WithMany().HasForeignKey(a => a.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LadderSnapshotRecord>(e =>
        {
            e.ToTable("ladder_snapshots");
            e.HasKey(s => new { s.Date, s.Category, s.PlayerId });
            e.Property(s => s.Category).HasConversion<string>().HasMaxLength(16);
            e.HasOne<PlayerRecord>().WithMany().HasForeignKey(s => s.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecapEpisodeRecord>(e =>
        {
            e.ToTable("recap_episodes");
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.Date).IsUnique();
            e.Property(r => r.Title).HasMaxLength(200);
            e.Property(r => r.Summary).HasMaxLength(2000);
            e.Property(r => r.Video).HasMaxLength(512);
            e.Property(r => r.Thumbnail).HasMaxLength(512);
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
