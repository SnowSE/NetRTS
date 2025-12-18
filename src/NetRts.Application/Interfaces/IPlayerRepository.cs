using NetRts.Domain.Entities;

namespace NetRts.Application.Interfaces;

/// <summary>
/// Repository interface for Player aggregate.
/// </summary>
public interface IPlayerRepository
{
    Task<Player?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Player?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<List<Player>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Player player, CancellationToken cancellationToken = default);
    Task UpdateAsync(Player player, CancellationToken cancellationToken = default);
    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default);
}
