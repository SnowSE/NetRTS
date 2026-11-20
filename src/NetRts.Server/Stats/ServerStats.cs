using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using NetRts.Protocol;

namespace NetRts.Server.Stats;

/// <summary>Runs one KQL query over [from, to) and answers its rows (each row's cells, in column order).</summary>
public delegate Task<IReadOnlyList<JsonElement[]>> LogsQuery(string kql, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

/// <summary>Turns the server's telemetry into <see cref="ServerStatsDto"/>: one query per figure.</summary>
public static class ServerStatsBuilder
{
    /// <summary>How far back to look: as long as Application Insights keeps data.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(90);

    private const string Http = """AppMetrics | where Name == "http.server.request.duration" """;

    public static async Task<ServerStatsDto> BuildAsync(LogsQuery query, DateTimeOffset to, string zone, DateTimeOffset now, CancellationToken ct)
    {
        var from = to - Window;
        Task<IReadOnlyList<JsonElement[]>> Q(string kql) => query(kql, from, to, ct);
        var tz = zone.Replace("\"", "", StringComparison.Ordinal);

        var first = await Q("AppMetrics | summarize min(TimeGenerated)");
        var codes = await Q(Http + """| summarize n = sum(ItemCount) by c = tostring(Properties["http.response.status_code"]) | order by n desc""");
        var routes = await Q(Http + """| extend r = tostring(Properties["http.route"]), m = tostring(Properties["http.request.method"]) | summarize commands = sumif(ItemCount, m == "POST" and r endswith "/commands"), polls = sumif(ItemCount, m == "GET" and r endswith "/state")""");
        var peak = await Q(Http + "| summarize n = sum(ItemCount) by m = bin(TimeGenerated, 1m) | top 1 by n");
        var latency = await Q("AppRequests | summarize p50 = percentile(DurationMs, 50), p99 = percentile(DurationMs, 99)");
        var ticks = await Q("""AppMetrics | where Name in ("netrts.tick.duration", "netrts.tick.overruns", "netrts.matches.live") | summarize ticks = sumif(ItemCount, Name == "netrts.tick.duration"), ms = sumif(Sum, Name == "netrts.tick.duration"), over = sumif(Sum, Name == "netrts.tick.overruns"), live = maxif(Max, Name == "netrts.matches.live")""");
        var errors = await Q("""AppExceptions | summarize n = sum(ItemCount), db = sumif(ItemCount, ExceptionType startswith "Npgsql")""");
        var restarts = await Q("""AppTraces | where Message startswith "Application started" | summarize n = count()""");
        var runtime = await Q("""AppMetrics | where Name in ("db.client.operation.duration", "dotnet.process.memory.working_set") | summarize q = sumif(ItemCount, Name == "db.client.operation.duration"), s = sumif(Sum, Name == "db.client.operation.duration"), mem = avgif(Sum / ItemCount, Name == "dotnet.process.memory.working_set"), memMax = maxif(Max, Name == "dotnet.process.memory.working_set")""");
        var health = await Q($"""AppAvailabilityResults | extend d = format_datetime(datetime_utc_to_local(TimeGenerated, "{tz}"), "yyyy-MM-dd") | summarize n = count(), ok = countif(Success == true) by d | order by d asc""");
        var places = await Q("AppAvailabilityResults | summarize dcount(Location)");
        var hours = await Q($"""AppRequests | extend t = datetime_utc_to_local(TimeGenerated, "{tz}") | summarize n = sum(ItemCount) by d = format_datetime(t, "yyyy-MM-dd"), h = hourofday(t) | order by d asc, h asc""");

        var statusCodes = codes.Where(r => Str(r[0]) != "").Select(r => new StatsCountDto(Str(r[0]), Long(r[1]))).ToList();
        var tickRow = One(ticks, 4);
        var runRow = One(runtime, 4);
        var dbQueries = Long(runRow[0]);
        var tickCount = Long(tickRow[0]);
        return new ServerStatsDto
        {
            GeneratedAt = now,
            From = Date(One(first, 1)[0]) ?? from,
            To = to,
            Requests = statusCodes.Sum(c => c.Count),
            StatusCodes = statusCodes,
            Commands = Long(One(routes, 2)[0]),
            StatePolls = Long(One(routes, 2)[1]),
            MedianMs = Dbl(One(latency, 2)[0]),
            P99Ms = Dbl(One(latency, 2)[1]),
            PeakMinute = Date(One(peak, 2)[0]),
            PeakMinuteRequests = Long(One(peak, 2)[1]),
            Ticks = tickCount,
            TickAverageMs = tickCount > 0 ? Dbl(tickRow[1]) / tickCount : 0,
            TickOverruns = Long(tickRow[2]),
            MostLiveMatches = (int)Long(tickRow[3]),
            Exceptions = (int)Long(One(errors, 2)[0]),
            DatabaseExceptions = (int)Long(One(errors, 2)[1]),
            Restarts = (int)Long(One(restarts, 1)[0]),
            DatabaseQueries = dbQueries,
            DatabaseAverageMs = dbQueries > 0 ? Dbl(runRow[1]) * 1000 / dbQueries : 0,   // the metric is in seconds
            MemoryAverageMb = Dbl(runRow[2]) / 1048576,
            MemoryPeakMb = Dbl(runRow[3]) / 1048576,
            Health = health.Select(r => new StatsHealthDayDto(Str(r[0]), (int)Long(r[1]), (int)Long(r[2]))).ToList(),
            HealthLocations = (int)Long(One(places, 1)[0]),
            Hours = hours.Select(r => new StatsHourDto(Str(r[0]), (int)Long(r[1]), Long(r[2]))).ToList(),
        };
    }

    /// <summary>The first row, or a row of nulls when the query found nothing.</summary>
    private static JsonElement[] One(IReadOnlyList<JsonElement[]> rows, int columns) =>
        rows.Count > 0 && rows[0].Length >= columns ? rows[0] : new JsonElement[columns];

    private static string Str(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Number => e.GetRawText(),
        _ => "",
    };

    private static double Dbl(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.String when double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => 0,
    };

    private static long Long(JsonElement e) => (long)Math.Round(Dbl(e));

    private static DateTimeOffset? Date(JsonElement e) =>
        e.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}

/// <summary>
/// Keeps <see cref="ServerStatsDto"/> for the Stats page: built shortly after startup and again each
/// morning at <see cref="RefreshHour"/> (local time), through the end of yesterday. Does nothing unless
/// <see cref="NetRtsOptions.LogAnalyticsWorkspaceId"/> is set.
/// </summary>
public sealed class ServerStatsService(IOptions<NetRtsOptions> options, IHttpClientFactory http, TimeProvider time,
    ILogger<ServerStatsService> logger) : BackgroundService
{
    public const int RefreshHour = 5;
    private static readonly string[] Scope = ["https://api.loganalytics.io/.default"];

    public ServerStatsDto? Current { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workspace = options.Value.LogAnalyticsWorkspaceId?.Trim();
        if (string.IsNullOrEmpty(workspace))
        {
            return;
        }

        // On Azure, the web app's own identity; on a laptop, whoever is signed in to the Azure CLI.
        TokenCredential credential = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT") is null
            ? new AzureCliCredential()
            : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
        var wait = TimeSpan.FromSeconds(20);   // let startup settle first
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(wait, time, stoppingToken);
                await RefreshAsync((kql, from, to, ct) => QueryAsync(credential, workspace, kql, from, to, ct), stoppingToken);
                wait = UntilNextMorning();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Stats: reading the server's telemetry failed; trying again in 30 minutes");
                wait = TimeSpan.FromMinutes(30);
            }
        }
    }

    /// <summary>Rebuilds <see cref="Current"/> through the end of yesterday (local time).</summary>
    public async Task RefreshAsync(LogsQuery query, CancellationToken ct)
    {
        logger.LogInformation("Stats: reading the server's telemetry");
        var now = time.GetUtcNow();
        var zone = options.Value.Zone;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var midnight = new DateTimeOffset(local.Date, zone.GetUtcOffset(local.Date));
        Current = await ServerStatsBuilder.BuildAsync(query, midnight.ToUniversalTime(), options.Value.TimeZone, now, ct);
        logger.LogInformation("Stats: server telemetry read through {To:u}", Current.To);
    }

    private TimeSpan UntilNextMorning()
    {
        var zone = options.Value.Zone;
        var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone);
        var next = local.Date.AddHours(RefreshHour);
        if (next <= local.DateTime)
        {
            next = next.AddDays(1);
        }

        return new DateTimeOffset(next, zone.GetUtcOffset(next)) - local;
    }

    private async Task<IReadOnlyList<JsonElement[]>> QueryAsync(TokenCredential credential, string workspace, string kql,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(Scope), ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.loganalytics.io/v1/workspaces/{Uri.EscapeDataString(workspace)}/query")
        {
            Content = JsonContent.Create(new { query = kql, timespan = $"{from.UtcDateTime:O}/{to.UtcDateTime:O}" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await http.CreateClient(nameof(ServerStatsService)).SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return json.RootElement.GetProperty("tables")[0].GetProperty("rows").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(cell => cell.Clone()).ToArray()).ToList();
    }
}
