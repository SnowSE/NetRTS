using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Application.Services;
using NetRts.Contracts.Responses;

namespace NetRts.ContractTests.Support;

public class TestContext
{
    public HttpClient HttpClient { get; set; } = null!;
    public Guid MatchId { get; set; }
    public Guid LobbyId { get; set; }
    public Guid Player1Id { get; set; }
    public Guid Player2Id { get; set; }
    public string Player1Token { get; set; } = string.Empty;
    public string Player2Token { get; set; } = string.Empty;
    public HttpResponseMessage? LastResponse { get; set; }
    public QueueCommandsResponse? LastCommandResponse { get; set; }
    public GameStateResponse? GameState { get; set; }
    public string? LastResponseBody { get; set; }
    public System.Net.HttpStatusCode LastStatusCode { get; set; }
    public IServiceProvider ServiceProvider { get; set; } = null!;

    public void SetAuthToken(string token, Guid? playerId = null)
    {
        HttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        if (playerId.HasValue)
        {
            HttpClient.DefaultRequestHeaders.Remove("X-Test-Player-Id");
            HttpClient.DefaultRequestHeaders.Add("X-Test-Player-Id", playerId.Value.ToString());
        }
    }

    public void ClearAuth()
    {
        HttpClient.DefaultRequestHeaders.Authorization = null;
        HttpClient.DefaultRequestHeaders.Remove("X-Test-Player-Id");
    }

    /// <summary>
    /// Gets the current command queue size for a player
    /// </summary>
    public async Task<int> GetQueueSizeAsync(Guid playerId)
    {
        using var scope = ServiceProvider.CreateScope();
        var queueManager = scope.ServiceProvider.GetRequiredService<ICommandQueueManager>();
        return await queueManager.GetQueueSizeAsync(MatchId, playerId);
    }

    /// <summary>
    /// Manually triggers game tick processing for testing purposes.
    /// This bypasses the background service and directly invokes the tick processor.
    /// </summary>
    public async Task ProcessGameTickAsync()
    {
        using var scope = ServiceProvider.CreateScope();
        var tickProcessor = scope.ServiceProvider.GetRequiredService<IGameTickProcessor>();

        // Process the tick for the specific match
        await tickProcessor.ProcessMatchTickAsync(MatchId, CancellationToken.None);
    }
}
