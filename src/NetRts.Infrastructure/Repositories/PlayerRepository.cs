using Microsoft.EntityFrameworkCore;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;
using NetRts.Infrastructure.Data;

namespace NetRts.Infrastructure.Repositories;

public class PlayerRepository : IPlayerRepository
{
    private readonly ApplicationDbContext _context;

    public PlayerRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Player?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Players
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Player?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await _context.Players
            .FirstOrDefaultAsync(p => p.Username == username, cancellationToken);
    }

    public async Task<List<Player>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Players
            .OrderByDescending(p => p.CurrentElo)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Player player, CancellationToken cancellationToken = default)
    {
        await _context.Players.AddAsync(player, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Player player, CancellationToken cancellationToken = default)
    {
        _context.Players.Update(player);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        return await _context.Players
            .AnyAsync(p => p.Username == username, cancellationToken);
    }
}
