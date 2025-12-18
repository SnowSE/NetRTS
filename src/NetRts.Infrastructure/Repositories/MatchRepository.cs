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
        _context.Matches.Update(match);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
