using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NetRts.Protocol;

namespace NetRts.Bots;

public sealed class NetRtsApiException(HttpStatusCode status, string code, string message)
    : Exception($"{(int)status} {code}: {message}")
{
    public HttpStatusCode Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>Thin typed wrapper over the NetRts REST API.</summary>
public sealed class NetRtsClient : IDisposable
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public NetRtsClient(Uri baseAddress, string? apiKey = null)
        : this(new HttpClient { BaseAddress = baseAddress }, apiKey, ownsClient: true)
    {
    }

    public NetRtsClient(HttpClient http, string? apiKey = null, bool ownsClient = false)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _ownsClient = ownsClient;
        _http.Timeout = TimeSpan.FromSeconds(100);
        if (apiKey is not null)
        {
            ApiKey = apiKey;
        }
    }

    public string? ApiKey
    {
        get => _http.DefaultRequestHeaders.Authorization?.Parameter;
        set => _http.DefaultRequestHeaders.Authorization = value is null ? null : new AuthenticationHeaderValue("Bearer", value);
    }

    public async Task<RegisterPlayerResponse> RegisterAsync(string name, CancellationToken ct = default)
    {
        var response = await Send<RegisterPlayerResponse>(HttpMethod.Post, "api/v1/players", new RegisterPlayerRequest { Name = name }, ct);
        ApiKey = response.ApiKey;
        return response;
    }

    public Task<PlayerProfileDto> GetMeAsync(CancellationToken ct = default) =>
        Send<PlayerProfileDto>(HttpMethod.Get, "api/v1/players/me", null, ct);

    public Task<RulesDto> GetRulesAsync(CancellationToken ct = default) =>
        Send<RulesDto>(HttpMethod.Get, "api/v1/rules", null, ct);

    public Task<IReadOnlyList<HouseBotDto>> GetHouseBotsAsync(CancellationToken ct = default) =>
        Send<IReadOnlyList<HouseBotDto>>(HttpMethod.Get, "api/v1/bots", null, ct);

    public Task<MatchSummaryDto> CreateMatchAsync(CreateMatchRequest request, CancellationToken ct = default) =>
        Send<MatchSummaryDto>(HttpMethod.Post, "api/v1/matches", request, ct);

    public Task<IReadOnlyList<MatchSummaryDto>> ListMatchesAsync(MatchStatus? status = null, CancellationToken ct = default) =>
        Send<IReadOnlyList<MatchSummaryDto>>(HttpMethod.Get, status is null ? "api/v1/matches" : $"api/v1/matches?status={status}", null, ct);

    public Task<MatchSummaryDto> GetMatchAsync(Guid matchId, CancellationToken ct = default) =>
        Send<MatchSummaryDto>(HttpMethod.Get, $"api/v1/matches/{matchId}", null, ct);

    public Task<MatchSummaryDto> JoinMatchAsync(Guid matchId, CancellationToken ct = default) =>
        Send<MatchSummaryDto>(HttpMethod.Post, $"api/v1/matches/{matchId}/join", null, ct);

    public Task<MatchSummaryDto> LeaveMatchAsync(Guid matchId, CancellationToken ct = default) =>
        Send<MatchSummaryDto>(HttpMethod.Post, $"api/v1/matches/{matchId}/leave", null, ct);

    /// <summary>Blocks (via repeated long-polls) until the match has started, then returns the opening state.</summary>
    public async Task<GameStateDto> WaitForStartAsync(Guid matchId, CancellationToken ct = default)
    {
        while (true)
        {
            try
            {
                return await GetStateAsync(matchId, waitForTick: 0, ct: ct).ConfigureAwait(false);
            }
            catch (NetRtsApiException ex) when (ex.Code == "MATCH_NOT_STARTED")
            {
                // The long-poll timed out while seats were still empty; keep waiting.
            }
        }
    }

    public Task<MapDto> GetMapAsync(Guid matchId, CancellationToken ct = default) =>
        Send<MapDto>(HttpMethod.Get, $"api/v1/matches/{matchId}/map", null, ct);

    /// <summary>
    /// Fetches your view of the match. With <paramref name="waitForTick"/> the server holds the
    /// request (long poll) until that tick has been simulated or the match ends.
    /// </summary>
    public Task<GameStateDto> GetStateAsync(Guid matchId, int? waitForTick = null, int? sinceTick = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (waitForTick is { } w)
        {
            query.Add($"waitForTick={w}");
        }

        if (sinceTick is { } s)
        {
            query.Add($"sinceTick={s}");
        }

        var url = $"api/v1/matches/{matchId}/state" + (query.Count > 0 ? "?" + string.Join('&', query) : "");
        return Send<GameStateDto>(HttpMethod.Get, url, null, ct);
    }

    public Task<SubmitCommandsResponse> SubmitAsync(Guid matchId, IReadOnlyList<CommandRequest> commands, CancellationToken ct = default) =>
        Send<SubmitCommandsResponse>(HttpMethod.Post, $"api/v1/matches/{matchId}/commands", new SubmitCommandsRequest { Commands = commands }, ct);

    public Task<MatchSummaryDto> SurrenderAsync(Guid matchId, CancellationToken ct = default) =>
        Send<MatchSummaryDto>(HttpMethod.Post, $"api/v1/matches/{matchId}/surrender", null, ct);

    public Task<MatchResultDto> GetResultAsync(Guid matchId, CancellationToken ct = default) =>
        Send<MatchResultDto>(HttpMethod.Get, $"api/v1/matches/{matchId}/result", null, ct);

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    private async Task<T> Send<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            ApiErrorDto? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(JsonOptions, ct).ConfigureAwait(false);
            }
            catch (JsonException)
            {
            }

            throw new NetRtsApiException(response.StatusCode, error?.Code ?? response.StatusCode.ToString(), error?.Message ?? response.ReasonPhrase ?? "");
        }

        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false))!;
    }
}

/// <summary>The standard bot loop: wait for each tick, decide, submit.</summary>
public static class BotLoop
{
    public static async Task<MatchResultDto?> RunAsync(
        NetRtsClient client,
        Guid matchId,
        IBotStrategy strategy,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(strategy);

        var rules = await client.GetRulesAsync(ct).ConfigureAwait(false);
        var state = await client.WaitForStartAsync(matchId, ct).ConfigureAwait(false);
        var map = await client.GetMapAsync(matchId, ct).ConfigureAwait(false);
        log?.Invoke($"Playing match {matchId} as slot {state.You.Slot} on a {map.Width}x{map.Height} map.");

        while (state.Status != MatchStatus.Completed && !ct.IsCancellationRequested)
        {
            if (state.Status == MatchStatus.Active)
            {
                var commands = strategy.Decide(new BotContext(state, map, rules));
                if (commands.Count > 0)
                {
                    var result = await client.SubmitAsync(matchId, commands, ct).ConfigureAwait(false);
                    foreach (var rejected in result.Results.Where(r => !r.Accepted))
                    {
                        log?.Invoke($"tick {state.Tick}: command {rejected.Index} ({commands[rejected.Index].Type}) rejected: {rejected.Error?.Code} {rejected.Error?.Message}");
                    }
                }
            }

            var lastTick = state.Tick;
            state = await client.GetStateAsync(matchId, waitForTick: lastTick + 1, sinceTick: lastTick, ct: ct).ConfigureAwait(false);
            foreach (var evt in state.Events.Where(e => e.Kind is "CommandFailed" or "PlayerEliminated" or "MatchEnded"))
            {
                log?.Invoke($"tick {evt.Tick}: {evt.Kind}: {evt.Message}");
            }
        }

        if (state.Status != MatchStatus.Completed)
        {
            return null;
        }

        return await client.GetResultAsync(matchId, ct).ConfigureAwait(false);
    }
}
