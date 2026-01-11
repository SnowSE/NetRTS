namespace NetRts.Contracts.Requests;

/// <summary>
/// Request to register a new player.
/// </summary>
public class RegisterPlayerRequest
{
    public required string Username { get; init; }
    public string? Password { get; init; }
    public string? Email { get; init; }
    public bool IsBot { get; init; }
}