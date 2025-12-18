using NetRts.Domain.Entities;
using NetRts.Domain.Enums;

namespace NetRts.Application.Interfaces;

/// <summary>
/// Repository interface for Match aggregate.
/// </summary>
public interface IMatchRepository
{
    Task<Match?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Match?> GetByIdWithEntitiesAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Match>> GetActiveMatchesAsync(CancellationToken cancellationToken = default);
    Task<List<Match>> GetPlayerMatchesAsync(Guid playerId, CancellationToken cancellationToken = default);
    Task AddAsync(Match match, CancellationToken cancellationToken = default);
    Task UpdateAsync(Match match, CancellationToken cancellationToken = default);
}
