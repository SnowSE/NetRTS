using System.Net;
using System.Net.Http.Json;
using NetRts.Bots;
using NetRts.Protocol;
using NetRts.Server.Endpoints;

namespace NetRts.Server.Tests;

/// <summary>Block Bot strategies saved to a website account (/api/v1/account/strategies).</summary>
public class AccountStrategyTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BlockProgramDto Strategy(string name, int workers = 10) => new()
    {
        Name = name,
        Switches = ["Attack!"],
        Rules =
        [
            new() { When = new() { Kind = "count", What = "workers", Op = "<", Value = workers }, Do = [new() { Kind = "train", Unit = UnitType.Worker }] },
            new() { When = new() { Kind = "switch", Name = "Attack!" }, Do = [new() { Kind = "attack", Who = "fighters" }] },
        ],
    };

    private static string NewId() => Guid.NewGuid().ToString();

    private static Task<HttpResponseMessage> SaveAsync(HttpClient http, string id, BlockProgramDto? program, int? baseVersion = null) =>
        http.PutAsJsonAsync($"/api/v1/account/strategies/{id}", new SaveAccountStrategyRequest { Program = program, BaseVersion = baseVersion }, NetRtsClient.JsonOptions, Ct);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(NetRtsClient.JsonOptions, Ct))!;

    private static async Task<List<AccountStrategyDto>> ListAsync(HttpClient http) =>
        (await http.GetFromJsonAsync<List<AccountStrategyDto>>("/api/v1/account/strategies", NetRtsClient.JsonOptions, Ct))!;

    private async Task<HttpClient> SignedInAsync() => factory.ClientFor(await factory.NewAccountAsync());

    [Fact]
    public async Task Saved_strategies_are_listed_and_each_save_moves_the_version_on()
    {
        var http = await SignedInAsync();
        var id = NewId();

        var created = await SaveAsync(http, id, Strategy("Rush"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = await ReadAsync<AccountStrategyDto>(created);
        Assert.Equal(1, first.Version);

        var updated = await SaveAsync(http, id, Strategy("Rush II", workers: 14), baseVersion: 1);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(2, (await ReadAsync<AccountStrategyDto>(updated)).Version);

        var listed = Assert.Single(await ListAsync(http));
        Assert.Equal(id, listed.Id);
        Assert.Equal(2, listed.Version);
        Assert.Equal("Rush II", listed.Program.Name);
        Assert.Equal(14, listed.Program.Rules[0].When.Value);
        Assert.Equal(UnitType.Worker, listed.Program.Rules[0].Do[0].Unit);
        Assert.Equal(DateTimeKind.Utc, listed.UpdatedAt.Kind);
        Assert.True(listed.UpdatedAt >= listed.CreatedAt);
    }

    [Fact]
    public async Task A_save_over_a_version_it_never_saw_gets_the_accounts_copy_back()
    {
        var http = await SignedInAsync();
        var id = NewId();
        await SaveAsync(http, id, Strategy("Here"));
        await SaveAsync(http, id, Strategy("There"), baseVersion: 1);

        // This computer last saw version 1; the account is on 2.
        var stale = await SaveAsync(http, id, Strategy("Stale"), baseVersion: 1);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var conflict = await ReadAsync<AccountStrategyConflictDto>(stale);
        Assert.Equal("STRATEGY_CHANGED", conflict.Code);
        Assert.Equal(2, conflict.Current!.Version);
        Assert.Equal("There", conflict.Current.Program.Name);

        // A "new" strategy the account already has is a conflict too.
        var again = await SaveAsync(http, id, Strategy("Again"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // And so is saving over one that's been deleted elsewhere.
        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/v1/account/strategies/{id}", Ct)).StatusCode);
        var gone = await SaveAsync(http, id, Strategy("Gone"), baseVersion: 2);
        Assert.Equal(HttpStatusCode.Conflict, gone.StatusCode);
        Assert.Null((await ReadAsync<AccountStrategyConflictDto>(gone)).Current);
        Assert.Empty(await ListAsync(http));
    }

    [Fact]
    public async Task Deleting_needs_the_version_you_saw_when_you_give_one()
    {
        var http = await SignedInAsync();
        var id = NewId();
        await SaveAsync(http, id, Strategy("Doomed"));
        await SaveAsync(http, id, Strategy("Doomed 2"), baseVersion: 1);

        var stale = await http.DeleteAsync($"/api/v1/account/strategies/{id}?version=1", Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(2, (await ReadAsync<AccountStrategyConflictDto>(stale)).Current!.Version);

        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/v1/account/strategies/{id}?version=2", Ct)).StatusCode);
        Assert.Empty(await ListAsync(http));
        var missing = await http.DeleteAsync($"/api/v1/account/strategies/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Another_account_cannot_see_or_touch_your_strategies()
    {
        var mine = await SignedInAsync();
        var theirs = await SignedInAsync();
        var id = NewId();
        await SaveAsync(mine, id, Strategy("Secret"));

        Assert.Empty(await ListAsync(theirs));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.DeleteAsync($"/api/v1/account/strategies/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SaveAsync(theirs, id, Strategy("Mine now"), baseVersion: 1)).StatusCode);

        // The same id on their account is their own strategy, separate from mine.
        Assert.Equal(HttpStatusCode.Created, (await SaveAsync(theirs, id, Strategy("Theirs"))).StatusCode);
        Assert.Equal("Secret", Assert.Single(await ListAsync(mine)).Program.Name);
        Assert.Equal("Theirs", Assert.Single(await ListAsync(theirs)).Program.Name);
    }

    [Fact]
    public async Task Programs_and_ids_are_checked()
    {
        var http = await SignedInAsync();

        var broken = Strategy("Broken") with { Rules = [new() { When = new() { Kind = "teleport" } }] };
        var invalid = await SaveAsync(http, NewId(), broken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("INVALID_PROGRAM", (await invalid.ErrorAsync()).Code);

        Assert.Equal("INVALID_PROGRAM", (await (await SaveAsync(http, NewId(), null)).ErrorAsync()).Code);

        var badId = await SaveAsync(http, "not a valid id!", Strategy("Fine"));
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
        Assert.Equal("INVALID_STRATEGY_ID", (await badId.ErrorAsync()).Code);
        Assert.Equal("INVALID_STRATEGY_ID", (await (await SaveAsync(http, new string('a', 65), Strategy("Fine"))).ErrorAsync()).Code);

        Assert.Empty(await ListAsync(http));
    }

    [Fact]
    public async Task An_account_keeps_at_most_a_hundred_strategies()
    {
        var http = await SignedInAsync();
        for (var i = 0; i < AccountStrategyEndpoints.MaxPerAccount; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await SaveAsync(http, $"s{i}", Strategy($"S{i}"))).StatusCode);
        }

        var one = await SaveAsync(http, "one-too-many", Strategy("Extra"));
        Assert.Equal(HttpStatusCode.Conflict, one.StatusCode);
        Assert.Equal("TOO_MANY_STRATEGIES", (await one.ErrorAsync()).Code);

        // Changing one it already has still works.
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(http, "s0", Strategy("S0 again"), baseVersion: 1)).StatusCode);
    }

    [Fact]
    public async Task Signed_out_visitors_and_bots_get_401()
    {
        var (_, bot) = await factory.NewPlayerAsync();
        var anonymous = factory.CreateClient();
        var withKey = factory.CreateClient();
        withKey.DefaultRequestHeaders.Authorization = new("Bearer", bot.ApiKey);

        foreach (var http in new[] { anonymous, withKey })
        {
            var list = await http.GetAsync("/api/v1/account/strategies", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
            Assert.Equal("NOT_SIGNED_IN", (await list.ErrorAsync()).Code);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SaveAsync(http, NewId(), Strategy("Nope"))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.DeleteAsync($"/api/v1/account/strategies/{NewId()}", Ct)).StatusCode);
        }
    }
}
