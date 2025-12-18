using Microsoft.EntityFrameworkCore;
using NetRts.Domain.Entities;

namespace NetRts.Application.Interfaces;

/// <summary>
/// Interface for the application database context.
/// Allows the application layer to access the database without depending on EF Core directly.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Player> Players { get; }
    DbSet<Match> Matches { get; }
    DbSet<Unit> Units { get; }
    DbSet<Building> Buildings { get; }
    DbSet<Command> Commands { get; }
    DbSet<MapTile> MapTiles { get; }
    DbSet<ResourceDeposit> ResourceDeposits { get; }
    DbSet<Upgrade> Upgrades { get; }
    DbSet<MatchLobby> MatchLobbies { get; }
    DbSet<LobbyPlayer> LobbyPlayers { get; }
    DbSet<PlayerScore> PlayerScores { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
