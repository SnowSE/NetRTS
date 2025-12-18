using Microsoft.EntityFrameworkCore;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;

namespace NetRts.Infrastructure.Data;

/// <summary>
/// Main database context for the NetRts application.
/// </summary>
public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Core game entities
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Command> Commands => Set<Command>();
    public DbSet<MapTile> MapTiles => Set<MapTile>();
    public DbSet<ResourceDeposit> ResourceDeposits => Set<ResourceDeposit>();
    public DbSet<Upgrade> Upgrades => Set<Upgrade>();

    // Lobby entities
    public DbSet<MatchLobby> MatchLobbies => Set<MatchLobby>();
    public DbSet<LobbyPlayer> LobbyPlayers => Set<LobbyPlayer>();

    // Leaderboard entities
    public DbSet<PlayerScore> PlayerScores => Set<PlayerScore>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Global query filters
        // Example: Soft delete filter (if implemented later)
        // modelBuilder.Entity<Match>().HasQueryFilter(m => !m.IsDeleted);
    }
}
