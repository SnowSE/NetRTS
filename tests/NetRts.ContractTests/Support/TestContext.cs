using System.Net.Http.Headers;
using NetRts.Contracts.Responses;

namespace NetRts.ContractTests.Support;

public class TestContext
{
    public HttpClient HttpClient { get; set; } = null!;
    public Guid MatchId { get; set; }
    public Guid Player1Id { get; set; }
    public Guid Player2Id { get; set; }
    public string Player1Token { get; set; } = string.Empty;
    public string Player2Token { get; set; } = string.Empty;
    public HttpResponseMessage? LastResponse { get; set; }
    public GameStateResponse? GameState { get; set; }

    public void SetAuthToken(string token)
    {
        HttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearAuth()
    {
        HttpClient.DefaultRequestHeaders.Authorization = null;
    }
}
