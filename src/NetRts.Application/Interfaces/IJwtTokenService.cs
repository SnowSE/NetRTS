using NetRts.Domain.Entities;

namespace NetRts.Application.Interfaces;

/// <summary>
/// Service for generating and validating JWT tokens.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generate a JWT token for a player.
    /// </summary>
    string GenerateToken(Player player);

    /// <summary>
    /// Validate a JWT token and extract player ID.
    /// </summary>
    Guid? ValidateToken(string token);
}
