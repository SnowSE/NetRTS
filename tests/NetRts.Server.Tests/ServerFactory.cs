using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Matches;

namespace NetRts.Server.Tests;

/// <summary>Real server pipeline over an isolated SQLite file. Ticks only advance when a test says so.</summary>
public class ServerFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"netrts-test-{Guid.NewGuid():N}.db");

    protected virtual bool AutoTick => false;

    public MatchManager Matches => Services.GetRequiredService<MatchManager>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:sqlite", $"Data Source={_dbPath}");
        builder.UseSetting("ConnectionStrings:netrtsdb", "");
        builder.UseSetting("NetRts:AutoTick", AutoTick.ToString());
        builder.UseSetting("NetRts:StartDelayMs", "0");
        builder.UseSetting("NetRts:MinTickIntervalMs", "10");
        builder.UseSetting("NetRts:MaxLongPollSeconds", "5");
        builder.UseSetting("NetRts:RequestsPerSecond", "100000");
        builder.UseSetting("NetRts:RegistrationsPerMinutePerIp", "100000");
    }

    /// <summary>Registers a uniquely named player and returns an authenticated client for it.</summary>
    public async Task<(NetRtsClient Client, RegisterPlayerResponse Player)> NewPlayerAsync(string prefix = "bot")
    {
        var client = new NetRtsClient(CreateClient());
        var player = await client.RegisterAsync($"{prefix}-{Guid.NewGuid():N}"[..20]);
        return (client, player);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class TickingServerFactory : ServerFactory
{
    protected override bool AutoTick => true;
}

internal static class HttpExtensions
{
    public static async Task<ApiErrorDto> ErrorAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiErrorDto>(NetRtsClient.JsonOptions))!;
}
