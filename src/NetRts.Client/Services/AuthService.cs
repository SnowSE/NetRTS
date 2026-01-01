using System.Net.Http.Json;
using Blazored.LocalStorage;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;

namespace NetRts.Client.Services;

/// <summary>
/// Manages authentication state for the client using JWT tokens.
/// </summary>
public class AuthService
{
    private const string TokenKey = "netrts_auth_token";
    private const string PlayerKey = "netrts_player";

    private readonly ILocalStorageService _localStorage;
    private readonly HttpClient _httpClient;

    private string? _cachedToken;
    private PlayerResponse? _cachedPlayer;

    public AuthService(ILocalStorageService localStorage, HttpClient httpClient)
    {
        _localStorage = localStorage;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Event raised when authentication state changes.
    /// </summary>
    public event Action? OnAuthStateChanged;

    /// <summary>
    /// Gets the current player ID, or Guid.Empty if not authenticated.
    /// </summary>
    public Guid PlayerId => _cachedPlayer?.PlayerId ?? Guid.Empty;

    /// <summary>
    /// Gets whether the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_cachedToken);

    /// <summary>
    /// Gets the current player info.
    /// </summary>
    public PlayerResponse? CurrentPlayer => _cachedPlayer;

    /// <summary>
    /// Gets the current JWT token.
    /// </summary>
    public string? Token => _cachedToken;

    /// <summary>
    /// Initialize auth state from local storage.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _cachedToken = await _localStorage.GetItemAsStringAsync(TokenKey);
            _cachedPlayer = await _localStorage.GetItemAsync<PlayerResponse>(PlayerKey);
        }
        catch
        {
            // Local storage may not be available during prerendering
            _cachedToken = null;
            _cachedPlayer = null;
        }
    }

    /// <summary>
    /// Register a new player.
    /// </summary>
    public async Task<(bool Success, string? Error)> RegisterAsync(string username, string? email, bool isBot)
    {
        try
        {
            var request = new RegisterPlayerRequest
            {
                Username = username,
                Email = email,
                IsBot = isBot
            };

            var response = await _httpClient.PostAsJsonAsync("api/v1/players", request);

            if (response.IsSuccessStatusCode)
            {
                var player = await response.Content.ReadFromJsonAsync<PlayerResponse>();
                if (player != null && !string.IsNullOrEmpty(player.Token))
                {
                    await SetAuthStateAsync(player.Token, player);
                    return (true, null);
                }
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            return (false, $"Registration failed: {errorContent}");
        }
        catch (Exception ex)
        {
            return (false, $"Registration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Login with existing username.
    /// </summary>
    public async Task<(bool Success, string? Error)> LoginAsync(string username)
    {
        try
        {
            var request = new LoginPlayerRequest { Username = username };
            var response = await _httpClient.PostAsJsonAsync("api/v1/players/login", request);

            if (response.IsSuccessStatusCode)
            {
                var player = await response.Content.ReadFromJsonAsync<PlayerResponse>();
                if (player != null && !string.IsNullOrEmpty(player.Token))
                {
                    await SetAuthStateAsync(player.Token, player);
                    return (true, null);
                }
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, "Player not found. Please register first.");
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            return (false, $"Login failed: {errorContent}");
        }
        catch (Exception ex)
        {
            return (false, $"Login failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Logout and clear authentication state.
    /// </summary>
    public async Task LogoutAsync()
    {
        _cachedToken = null;
        _cachedPlayer = null;

        try
        {
            await _localStorage.RemoveItemAsync(TokenKey);
            await _localStorage.RemoveItemAsync(PlayerKey);
        }
        catch
        {
            // Ignore storage errors
        }

        OnAuthStateChanged?.Invoke();
    }

    /// <summary>
    /// Refresh player profile from server.
    /// </summary>
    public async Task RefreshProfileAsync()
    {
        if (!IsAuthenticated) return;

        try
        {
            var response = await _httpClient.GetAsync("api/v1/players/me");
            if (response.IsSuccessStatusCode)
            {
                var player = await response.Content.ReadFromJsonAsync<PlayerResponse>();
                if (player != null)
                {
                    _cachedPlayer = player;
                    await _localStorage.SetItemAsync(PlayerKey, player);
                }
            }
        }
        catch
        {
            // Ignore refresh errors
        }
    }

    private async Task SetAuthStateAsync(string token, PlayerResponse player)
    {
        _cachedToken = token;
        _cachedPlayer = player;

        try
        {
            await _localStorage.SetItemAsStringAsync(TokenKey, token);
            await _localStorage.SetItemAsync(PlayerKey, player);
        }
        catch
        {
            // Ignore storage errors
        }

        OnAuthStateChanged?.Invoke();
    }
}
