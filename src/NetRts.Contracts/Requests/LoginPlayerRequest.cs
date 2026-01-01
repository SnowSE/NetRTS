namespace NetRts.Contracts.Requests;

/// <summary>
/// Request DTO for player login.
/// </summary>
public class LoginPlayerRequest
{
    /// <summary>
    /// Player username.
    /// </summary>
    public string Username { get; set; } = string.Empty;
}
