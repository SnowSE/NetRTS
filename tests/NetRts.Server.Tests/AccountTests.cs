using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;

namespace NetRts.Server.Tests;

/// <summary>Discord accounts and claimed bots.</summary>
public class AccountTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_visitors_see_that_nobody_is_signed_in()
    {
        var account = await factory.CreateClient().GetFromJsonAsync<AccountDto>("/api/v1/account", NetRtsClient.JsonOptions, Ct);

        Assert.False(account!.SignedIn);
        Assert.False(account.DiscordEnabled);
        Assert.Null(account.Account);
    }

    [Fact]
    public async Task Account_endpoints_need_a_discord_sign_in_not_an_api_key()
    {
        var (_, bot) = await factory.NewPlayerAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", bot.ApiKey);

        var response = await http.PostAsJsonAsync("/api/v1/account/bots/claim", new ClaimBotRequest { ApiKey = bot.ApiKey }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("NOT_SIGNED_IN", (await response.ErrorAsync()).Code);
    }

    [Fact]
    public async Task Claiming_a_bot_gives_it_a_blue_check_and_the_owners_nickname()
    {
        var (client, bot) = await factory.NewPlayerAsync();
        var account = await factory.NewAccountAsync(nickname: "Buster");
        var http = factory.ClientFor(account);
        Assert.False((await client.GetMeAsync(Ct)).Verified);

        var claim = await http.PostAsJsonAsync("/api/v1/account/bots/claim", new ClaimBotRequest { ApiKey = bot.ApiKey }, Ct);

        claim.EnsureSuccessStatusCode();
        var me = await client.GetMeAsync(Ct);
        Assert.True(me.Verified);
        Assert.Equal("Buster", me.OwnerName);
        var mine = await http.GetFromJsonAsync<AccountDto>("/api/v1/account", NetRtsClient.JsonOptions, Ct);
        Assert.True(mine!.SignedIn);
        Assert.Equal("Buster", mine.Account!.DisplayName);
        Assert.Contains(mine.Bots, b => b.PlayerId == bot.PlayerId);

        // Someone else can't take it, and a wrong key finds nothing.
        var other = factory.ClientFor(await factory.NewAccountAsync());
        var stolen = await other.PostAsJsonAsync("/api/v1/account/bots/claim", new ClaimBotRequest { ApiKey = bot.ApiKey }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);
        var unknown = await other.PostAsJsonAsync("/api/v1/account/bots/claim", new ClaimBotRequest { ApiKey = "nrts_nope" }, Ct);
        Assert.Equal("UNKNOWN_API_KEY", (await unknown.ErrorAsync()).Code);
    }

    [Fact]
    public async Task Owners_can_register_bots_and_replace_their_keys()
    {
        var http = factory.ClientFor(await factory.NewAccountAsync());
        var created = await http.PostAsJsonAsync("/api/v1/account/bots", new RegisterPlayerRequest { Name = $"own-{Guid.NewGuid():N}"[..16] }, Ct);
        var bot = (await created.Content.ReadFromJsonAsync<RegisterPlayerResponse>(NetRtsClient.JsonOptions, Ct))!;
        var oldClient = Client(bot.ApiKey);
        Assert.True((await oldClient.GetMeAsync(Ct)).Verified);

        var replaced = await http.PostAsync($"/api/v1/account/bots/{bot.PlayerId}/key", null, Ct);
        var fresh = (await replaced.Content.ReadFromJsonAsync<RegisterPlayerResponse>(NetRtsClient.JsonOptions, Ct))!;

        Assert.NotEqual(bot.ApiKey, fresh.ApiKey);
        await Assert.ThrowsAsync<NetRtsApiException>(() => oldClient.GetMeAsync(Ct));
        Assert.Equal(bot.PlayerId, (await Client(fresh.ApiKey).GetMeAsync(Ct)).PlayerId);

        // Another account can't touch it.
        var other = factory.ClientFor(await factory.NewAccountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/v1/account/bots/{bot.PlayerId}/key", null, Ct)).StatusCode);
    }

    [Fact]
    public void With_a_discord_server_configured_only_its_members_are_verified()
    {
        var anyone = new DiscordOptions();
        var server = new DiscordOptions { ServerId = "123" };
        var member = new AccountRecord { InServer = true };
        var outsider = new AccountRecord { InServer = false };

        Assert.False(Accounts.IsVerified(null, anyone));
        Assert.True(Accounts.IsVerified(outsider, anyone));
        Assert.True(Accounts.IsVerified(member, server));
        Assert.False(Accounts.IsVerified(outsider, server));
    }

    [Fact]
    public async Task Databases_made_before_migrations_are_upgraded_in_place()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netrts-legacy-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<SqliteNetRtsDb>().UseSqlite($"Data Source={path}").Options;
        try
        {
            // What EnsureCreated used to build: the first migration's tables, with no migration history.
            await using (var legacy = new SqliteNetRtsDb(options))
            {
                var initial = legacy.Database.GetMigrations().First();
                await legacy.GetService<IMigrator>().MigrateAsync(initial, Ct);
                await legacy.Database.ExecuteSqlRawAsync("DROP TABLE __EFMigrationsHistory", Ct);
                await legacy.Database.ExecuteSqlRawAsync(
                    "INSERT INTO players (Id, Name, ApiKeyHash, IsHouseBot, Rating, Wins, Losses, Draws, CreatedAt) " +
                    "VALUES ('6f2c1d4e-0000-0000-0000-000000000001', 'oldbot', 'abc', 0, 1300, 5, 2, 0, '2026-01-01 00:00:00')", Ct);
            }

            await using (var db = new SqliteNetRtsDb(options))
            {
                await DatabaseMigrator.MigrateAsync(db, NullLogger.Instance, Ct);

                Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
                var bot = await db.Players.SingleAsync(p => p.Name == "oldbot", Ct);
                Assert.Equal("abc", bot.ApiKeyHash);
                Assert.Empty(await db.PlayerStats.ToListAsync(Ct)); // every ladder starts from zero
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private NetRtsClient Client(string apiKey) => new(factory.CreateClient(), apiKey);

    [Fact]
    public async Task Only_admins_get_the_telemetry_links()
    {
        var admin = await factory.ClientFor(await factory.NewAccountAsync(admin: true))
            .GetFromJsonAsync<AccountDto>("/api/v1/account", NetRtsClient.JsonOptions, Ct);
        var player = await factory.ClientFor(await factory.NewAccountAsync())
            .GetFromJsonAsync<AccountDto>("/api/v1/account", NetRtsClient.JsonOptions, Ct);

        Assert.Empty(player!.TelemetryLinks);
        Assert.Contains(admin!.TelemetryLinks, l => l.Label == "Failures" &&
            l.Url == $"https://portal.azure.com/#/resource{ServerFactory.TestAppInsights}/failures");
        var exceptions = admin.TelemetryLinks.Single(l => l.Label == "Exceptions").Url;
        var q = Uri.UnescapeDataString(exceptions.Split("/q/")[1].Split('/')[0]);
        using var gzip = new System.IO.Compression.GZipStream(new MemoryStream(Convert.FromBase64String(q)), System.IO.Compression.CompressionMode.Decompress);
        Assert.StartsWith("exceptions\n| project", new StreamReader(gzip).ReadToEnd());
    }
}
