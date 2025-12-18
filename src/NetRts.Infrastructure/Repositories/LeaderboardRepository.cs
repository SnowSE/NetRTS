using Microsoft.EntityFrameworkCore;
using NetRts.Application.Interfaces;
using NetRts.Domain.Entities;
using NetRts.Infrastructure.Data;

namespace NetRts.Infrastructure.Repositories;

public class LeaderboardRepository : ILeaderboardRepository
{
    private readonly ApplicationDbContext _context;

    public LeaderboardRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<PlayerScore>> GetTopPlayersAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        return await _context.PlayerScores
            .OrderByDescending(ps => ps.TotalScore)
            .ThenByDescending(ps => ps.WinRate)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<PlayerScore?> GetPlayerScoreAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        return await _context.PlayerScores
            .FirstOrDefaultAsync(ps => ps.PlayerId == playerId, cancellationToken);
    }

    public async Task UpsertPlayerScoreAsync(PlayerScore playerScore, CancellationToken cancellationToken = default)
    {
        var existing = await GetPlayerScoreAsync(playerScore.PlayerId, cancellationToken);
        if (existing == null)
        {
            await _context.PlayerScores.AddAsync(playerScore, cancellationToken);
        }
        else
        {
            _context.PlayerScores.Update(playerScore);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecalculateRanksAsync(CancellationToken cancellationToken = default)
    {
        var allScores = await _context.PlayerScores
            .OrderByDescending(ps => ps.TotalScore)
            .ToListAsync(cancellationToken);

        int rank = 1;
        foreach (var score in allScores)
        {
            score.SetRank(rank++);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
