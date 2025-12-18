using Microsoft.EntityFrameworkCore;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Infrastructure.Data;

namespace NetRts.Infrastructure.Repositories;

public class MatchLobbyRepository : IMatchLobbyRepository
{
    private readonly ApplicationDbContext _context;

    public MatchLobbyRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MatchLobby?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.MatchLobbies
            .Include(l => l.Players)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
    }

    public async Task<List<MatchLobby>> GetOpenLobbiesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.MatchLobbies
            .Include(l => l.Players)
            .Where(l => l.Status == LobbyStatus.Open || l.Status == LobbyStatus.Full)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(MatchLobby lobby, CancellationToken cancellationToken = default)
    {
        await _context.MatchLobbies.AddAsync(lobby, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(MatchLobby lobby, CancellationToken cancellationToken = default)
    {
        _context.MatchLobbies.Update(lobby);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var lobby = await GetByIdAsync(id, cancellationToken);
        if (lobby != null)
        {
            _context.MatchLobbies.Remove(lobby);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
