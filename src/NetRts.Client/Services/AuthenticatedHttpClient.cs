using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NetRts.Client.Services;

/// <summary>
/// An HttpClient wrapper that automatically adds authentication headers to requests.
/// Use this service instead of injecting HttpClient directly when making authenticated API calls.
/// </summary>
public class AuthenticatedHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly AuthService _authService;

    public AuthenticatedHttpClient(HttpClient httpClient, AuthService authService)
    {
        _httpClient = httpClient;
        _authService = authService;
    }

    /// <summary>
    /// Gets the base address of the HttpClient.
    /// </summary>
    public Uri? BaseAddress => _httpClient.BaseAddress;

    /// <summary>
    /// Sends an HTTP request with authentication headers.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        AddAuthHeaders(request);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sends a GET request to the specified URI with authentication headers.
    /// </summary>
    public async Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        AddAuthHeaders(request);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sends a GET request and deserializes the JSON response.
    /// </summary>
    public async Task<T?> GetFromJsonAsync<T>(string requestUri, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    /// <summary>
    /// Sends a POST request with JSON content and authentication headers.
    /// </summary>
    public async Task<HttpResponseMessage> PostAsJsonAsync<T>(string requestUri, T value, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(value)
        };
        AddAuthHeaders(request);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sends a POST request without a body and with authentication headers.
    /// </summary>
    public async Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent? content = null, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = content
        };
        AddAuthHeaders(request);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sends a PUT request with JSON content and authentication headers.
    /// </summary>
    public async Task<HttpResponseMessage> PutAsJsonAsync<T>(string requestUri, T value, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = JsonContent.Create(value)
        };
        AddAuthHeaders(request);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Sends a DELETE request with authentication headers.
    /// </summary>
    public async Task<HttpResponseMessage> DeleteAsync(string requestUri, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
        AddAuthHeaders(request);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        // Add JWT token if available
        if (_authService.IsAuthenticated && !string.IsNullOrEmpty(_authService.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authService.Token);
        }

        // Also add player ID header as fallback for development
        if (_authService.CurrentPlayer != null)
        {
            request.Headers.Remove("X-Test-Player-Id");
            request.Headers.Add("X-Test-Player-Id", _authService.CurrentPlayer.PlayerId.ToString());
        }
    }
}
