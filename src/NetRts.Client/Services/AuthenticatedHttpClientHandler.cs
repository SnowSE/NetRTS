using System.Net.Http.Headers;

namespace NetRts.Client.Services;

/// <summary>
/// Delegating handler that adds authentication headers to outgoing HTTP requests.
/// </summary>
public class AuthenticatedHttpClientHandler : DelegatingHandler
{
    private readonly AuthService _authService;

    public AuthenticatedHttpClientHandler(AuthService authService)
    {
        _authService = authService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
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

        return await base.SendAsync(request, cancellationToken);
    }
}
