using NetRts.Domain.Entities;
using NetRts.Domain.Enums;

namespace NetRts.Application.Interfaces;

public interface IMatchLobbyRepository
{
    Task<MatchLobby?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<MatchLobby>> GetOpenLobbiesAsync(CancellationToken cancellationToken = default);
    Task AddAsync(MatchLobby lobby, CancellationToken cancellationToken = default);
    Task UpdateAsync(MatchLobby lobby, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
