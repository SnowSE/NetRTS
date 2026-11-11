using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using NetRts.Bots;
using NetRts.Protocol;

namespace NetRts.Server.Tests;

/// <summary>A bot can have three practice matches running at once; matches it has walked away from don't count.</summary>
public class RunningMatchCapTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CreateMatchRequest VersusSitter = new() { HouseBots = ["sitter"] };

    [Fact]
    public async Task A_fourth_running_match_is_refused_until_one_ends()
    {
        var (bot, _) = await factory.NewPlayerAsync();
        var running = new List<MatchSummaryDto>();
        for (var i = 0; i < 3; i++)
        {
            running.Add(await bot.CreateMatchAsync(VersusSitter, Ct));
        }

        var refused = await Assert.ThrowsAsync<NetRtsApiException>(() => bot.CreateMatchAsync(VersusSitter, Ct));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.Status);
        Assert.Equal("TOO_MANY_RUNNING_MATCHES", refused.Code);
        Assert.Contains(running[0].MatchId.ToString(), refused.Message);
        // Being played: none of them was given up to make room.
        Assert.All(running, m => Assert.Equal(MatchStatus.Active, factory.Matches.Get(m.MatchId)!.Status));

        await bot.SurrenderAsync(running[0].MatchId, Ct);

        var fourth = await bot.CreateMatchAsync(VersusSitter, Ct);
        Assert.Equal(MatchStatus.Active, fourth.Status);
    }

    [Fact]
    public async Task Other_bots_have_their_own_allowance()
    {
        var (first, _) = await factory.NewPlayerAsync();
        var (second, _) = await factory.NewPlayerAsync();
        for (var i = 0; i < 3; i++)
        {
            await first.CreateMatchAsync(VersusSitter, Ct);
        }

        var match = await second.CreateMatchAsync(VersusSitter, Ct);

        Assert.Equal(MatchStatus.Active, match.Status);
    }

    [Fact]
    public async Task A_match_the_bot_was_knocked_out_of_doesnt_count()
    {
        var (bot, _) = await factory.NewPlayerAsync();
        // Three house bots play on after this bot surrenders.
        var crowded = await bot.CreateMatchAsync(new CreateMatchRequest { MaxPlayers = 4, HouseBots = ["sitter", "sitter", "sitter"] }, Ct);
        await bot.SurrenderAsync(crowded.MatchId, Ct);
        Assert.Equal(MatchStatus.Active, factory.Matches.Get(crowded.MatchId)!.Status);

        for (var i = 0; i < 3; i++)
        {
            await bot.CreateMatchAsync(VersusSitter, Ct);
        }
    }
}

/// <summary>Starting a match past the cap surrenders the ones the bot stopped playing (it crashed, or was stopped and restarted).</summary>
public class AbandonedMatchTests(AbandonedMatchTests.Factory factory) : IClassFixture<AbandonedMatchTests.Factory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Every match counts as left the moment it's created.</summary>
    public sealed class Factory : ServerFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("NetRts:AbandonedMatchSeconds", "0");
        }
    }

    [Fact]
    public async Task Abandoned_matches_are_surrendered_to_make_room()
    {
        var (bot, player) = await factory.NewPlayerAsync();
        var left = new List<MatchSummaryDto>();
        for (var i = 0; i < 3; i++)
        {
            left.Add(await bot.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct));
        }

        var fresh = await bot.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);

        Assert.Equal(MatchStatus.Active, fresh.Status);
        Assert.All(left, m =>
        {
            var host = factory.Matches.Get(m.MatchId)!;
            Assert.Equal(MatchStatus.Completed, host.Status);
            Assert.False(host.IsPlaying(player.PlayerId));
        });
    }

    [Fact]
    public async Task A_block_bot_match_plays_on_when_the_same_key_starts_coded_matches()
    {
        var (bot, player) = await factory.NewPlayerAsync();
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", player.ApiKey);
        var program = new BlockProgramDto { Rules = [new() { Do = [new() { Kind = "train", Unit = UnitType.Worker }] }] };
        var start = await http.PostAsJsonAsync("/api/v1/blocks/matches",
            new StartBlockMatchRequest { Program = program, Mode = BlockPlayMode.Sandbox }, NetRtsClient.JsonOptions, Ct);
        var blocks = (await start.Content.ReadFromJsonAsync<MatchSummaryDto>(NetRtsClient.JsonOptions, Ct))!;
        var coded = new List<MatchSummaryDto>();
        for (var i = 0; i < 2; i++)
        {
            coded.Add(await bot.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct));
        }

        // At the cap, with every match "left" (this factory counts them as left at once): the coded ones make
        // room, the Block Bot one keeps playing.
        await bot.CreateMatchAsync(new CreateMatchRequest { HouseBots = ["sitter"] }, Ct);

        Assert.Equal(MatchStatus.Active, factory.Matches.Get(blocks.MatchId)!.Status);
        Assert.All(coded, m => Assert.Equal(MatchStatus.Completed, factory.Matches.Get(m.MatchId)!.Status));
    }
}
