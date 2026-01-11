using Microsoft.EntityFrameworkCore;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Infrastructure.Data;

namespace NetRts.Infrastructure.Repositories;

public class MatchRepository : IMatchRepository
{
    private readonly ApplicationDbContext _context;

    public MatchRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Match?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Matches
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<Match?> GetByIdWithEntitiesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Matches
            .Include(m => m.Units)
            .Include(m => m.Buildings)
            .Include(m => m.Commands)
            .Include(m => m.MapTiles)
            .Include(m => m.ResourceDeposits)
            .Include(m => m.Upgrades)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<List<Match>> GetActiveMatchesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Matches
            .Where(m => m.Status == MatchStatus.Active)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Match>> GetPlayerMatchesAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        return await _context.Matches
            .Where(m => m.Player1Id == playerId || m.Player2Id == playerId)
            .OrderByDescending(m => m.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Match match, CancellationToken cancellationToken = default)
    {
        await _context.Matches.AddAsync(match, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Match match, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsRelational())
        {
            await _context.Matches
                .Where(m => m.Id == match.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, match.Status)
                    .SetProperty(m => m.CurrentTick, match.CurrentTick)
                    .SetProperty(m => m.StartedAt, match.StartedAt)
                    .SetProperty(m => m.EndedAt, match.EndedAt)
                    .SetProperty(m => m.WinnerId, match.WinnerId)
                    .SetProperty(m => m.Player1Resources, match.Player1Resources)
                    .SetProperty(m => m.Player2Resources, match.Player2Resources)
                    .SetProperty(m => m.GameStateSnapshot, match.GameStateSnapshot)
                    // Player 1 Score
                    .SetProperty(m => m.Player1Score.UnitsDestroyed, match.Player1Score.UnitsDestroyed)
                    .SetProperty(m => m.Player1Score.BuildingsDestroyed, match.Player1Score.BuildingsDestroyed)
                    .SetProperty(m => m.Player1Score.ResourcesGathered, match.Player1Score.ResourcesGathered)
                    .SetProperty(m => m.Player1Score.UnitsRemaining, match.Player1Score.UnitsRemaining)
                    .SetProperty(m => m.Player1Score.BuildingsRemaining, match.Player1Score.BuildingsRemaining)
                    // Player 2 Score
                    .SetProperty(m => m.Player2Score.UnitsDestroyed, match.Player2Score.UnitsDestroyed)
                    .SetProperty(m => m.Player2Score.BuildingsDestroyed, match.Player2Score.BuildingsDestroyed)
                    .SetProperty(m => m.Player2Score.ResourcesGathered, match.Player2Score.ResourcesGathered)
                    .SetProperty(m => m.Player2Score.UnitsRemaining, match.Player2Score.UnitsRemaining)
                    .SetProperty(m => m.Player2Score.BuildingsRemaining, match.Player2Score.BuildingsRemaining)
                , cancellationToken);
        }
        else
        {
            // Fallback for In-Memory provider (and others not supporting ExecuteUpdate)
            // To avoid attaching the whole graph, we find the tracked instance or load it.
            var trackedMatch = _context.Matches.Local.FirstOrDefault(m => m.Id == match.Id);
            if (trackedMatch == null)
            {
                trackedMatch = await _context.Matches.FindAsync(new object[] { match.Id }, cancellationToken);
            }

            if (trackedMatch != null)
            {
                // Update only root properties and owned types
                _context.Entry(trackedMatch).CurrentValues.SetValues(match);
                _context.Entry(trackedMatch).Reference(m => m.Player1Score).TargetEntry?.CurrentValues.SetValues(match.Player1Score);
                _context.Entry(trackedMatch).Reference(m => m.Player2Score).TargetEntry?.CurrentValues.SetValues(match.Player2Score);
                
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
