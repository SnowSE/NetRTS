using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Bots;
using NetRts.Engine;
using NetRts.Protocol;
using NetRts.Server.Achievements;
using NetRts.Server.Auth;
using NetRts.Server.Data;
using NetRts.Server.Endpoints;
using NetRts.Server.Matches;

namespace NetRts.Server.Recaps;

/// <summary>Gathers one day's facts for the recap show (<see cref="RecapBriefDto"/>).</summary>
public sealed class RecapBrief(
    NetRtsDb db,
    LeagueMatchManager leagues,
    IOptions<NetRtsOptions> options,
    IOptions<RecapOptions> recap,
    IOptions<DiscordOptions> discord)
{
    private const int MaxListed = 8;
    private Dictionary<Guid, PlayerRecord> _players = [];

    public async Task<RecapBriefDto> BuildAsync(DateOnly day, CancellationToken ct)
    {
        var zone = options.Value.Zone;
        var (start, end) = LadderSnapshots.Window(day, zone);
        var rows = await db.MatchPlayers.AsNoTracking().Where(r => r.CompletedAt >= start && r.CompletedAt < end).ToListAsync(ct);
        var byMatch = rows.ToLookup(r => r.MatchId);
        var realMatchIds = byMatch.Where(g => g.Any(r => r.RealOpponents > 0)).Select(g => g.Key).ToList();
        var leagueMatchIds = byMatch.Where(g => g.First().Category != MatchCategory.Practice).Select(g => g.Key).ToList();
        var detailIds = realMatchIds.Union(leagueMatchIds).ToList();
        var matches = await db.Matches.AsNoTracking().Where(m => detailIds.Contains(m.Id))
            .Select(m => new { m.Id, m.MaxTicks, m.ResultJson }).ToListAsync(ct);
        var results = matches.ToDictionary(m => m.Id, m => (Result: JsonSerializer.Deserialize<MatchResultDto>(m.ResultJson, NetRtsClient.JsonOptions)!, m.MaxTicks));

        var achievements = await db.BotAchievements.AsNoTracking().Where(a => a.EarnedAt >= start && a.EarnedAt < end).ToListAsync(ct);
        var ids = rows.Select(r => r.PlayerId).Concat(achievements.Select(a => a.PlayerId)).ToHashSet();
        var newlyRegistered = await db.Players.AsNoTracking().Where(p => !p.IsHouseBot && p.CreatedAt >= start && p.CreatedAt < end).Select(p => p.Id).ToListAsync(ct);
        ids.UnionWith(newlyRegistered);
        _players = await db.Players.AsNoTracking().Include(p => p.Owner).Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var real = rows.Where(r => !r.IsHouseBot && r.RealOpponents > 0).ToList();
        var episodes = await db.RecapEpisodes.AsNoTracking().OrderByDescending(e => e.Date).Take(5).Select(e => e.Title).ToListAsync(ct);
        return new RecapBriefDto
        {
            Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Weekday = day.DayOfWeek.ToString(),
            Quiet = realMatchIds.Count < recap.Value.QuietNightMatches,
            Lore = Lore(),
            Totals = new RecapTotalsDto
            {
                Matches = byMatch.Count,
                RealMatches = realMatchIds.Count,
                LeagueMatches = leagueMatchIds.Count,
                BotsActive = rows.Where(r => !r.IsHouseBot).Select(r => r.PlayerId).Distinct().Count(),
                OreGathered = rows.Sum(r => (long)r.Gathered),
                UnitsKilled = rows.Sum(r => (long)r.UnitsKilled),
                LongestMatchTicks = rows.Count > 0 ? rows.Max(r => r.Ticks) : 0,
            },
            NewBots = await NewBotsAsync(rows, newlyRegistered, start, ct),
            Ladders = await LaddersAsync(day, ct),
            Movers = Movers(rows),
            Achievements = await AchievementsAsync(achievements, start, ct),
            Strategies = await StrategiesAsync(rows, start, ct),
            MatchOfTheNight = MatchOfTheNight(realMatchIds.Union(leagueMatchIds), byMatch, results),
            PowerPlays = PowerPlays(real, results),
            Surprises = await SurprisesAsync(real, byMatch, results, start, ct),
            Rivalries = Rivalries(real, byMatch),
            MissingPersons = await MissingAsync(end, ct),
            Upcoming = Upcoming(day),
            Mentoring = recap.Value.Mentoring,
            RecentEpisodes = episodes,
            RulesNotes = GameRules.ToDto().Notes,
        };
    }

    // ------------------------------------------------------------------ names

    private bool Snow => !string.Equals(recap.Value.Brand, "classic", StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Nicknames = new()
    {
        ["sitter"] = "Sleepy Sitter",
        ["rusher"] = "Honey Badger",
        ["balanced"] = "Blue Badger",
        ["economist"] = "Grub Hoarder",
    };

    /// <summary>A bot's name as the hosts say it: house bots by their nickname.</summary>
    private string Name(Guid id) => _players.TryGetValue(id, out var p) ? Display(p.Name) : "a bot";

    private string Display(string name)
    {
        if (!name.StartsWith("house-", StringComparison.Ordinal))
        {
            return name;
        }

        var bot = name[6..].Split(' ')[0];
        return Snow && Nicknames.TryGetValue(bot, out var nick) ? $"{nick} (house bot)" : $"{bot} (house bot)";
    }

    private string? Owner(Guid id) =>
        _players.TryGetValue(id, out var p) && Accounts.IsVerified(p.Owner, discord.Value) ? p.Owner!.DisplayName : null;

    private RecapLoreDto Lore() => Snow
        ? new RecapLoreDto
        {
            Game = "Badger Brawl",
            Ore = "grubs",
            Setting = "Snow College in Ephraim, Utah (Go Badgers). Bots are student-written programs; their units are badgers.",
            Names = new Dictionary<string, string>
            {
                ["house-sitter"] = "Sleepy Sitter",
                ["house-rusher"] = "Honey Badger",
                ["house-balanced"] = "Blue Badger",
                ["house-economist"] = "Grub Hoarder",
                ["CommandCenter"] = "HQ (each team's is a campus building)",
                ["Barracks"] = "Housing",
                ["ResourceDepot"] = "Co-op Store",
                ["TechLab"] = "GRSC Makerspace",
                ["GuardTower"] = "The Bell Tower",
                ["SiegeWorkshop"] = "Washburn Welding Shop (Richfield campus)",
                ["Trebuchet"] = "Snow Mobile (an old Snap-on tool truck in Snow College colours, with a cannon on the roof)",
                ["Weapons1"] = "Intro to Chemistry",
                ["Weapons2"] = "Organic Chemistry",
                ["Armor1"] = "Unit Testing",
                ["Armor2"] = "Code Review",
                ["Mobility1"] = "Algorithms",
                ["Mobility2"] = "Parallel Computing",
                ["Harvesting1"] = "Intro to Biology",
                ["Harvesting2"] = "Entomology",
                ["SiegeEngineering"] = "Welding Technology",
            },
        }
        : new RecapLoreDto { Game = "NetRTS", Ore = "ore", Setting = "A real-time strategy game played by bots over a REST API." };

    // ------------------------------------------------------------------ sections

    private async Task<IReadOnlyList<RecapBotDto>> NewBotsAsync(List<MatchPlayerRecord> rows, List<Guid> registered, DateTime start, CancellationToken ct)
    {
        var active = rows.Where(r => !r.IsHouseBot).Select(r => r.PlayerId).Distinct().ToList();
        var veterans = await db.MatchPlayers.Where(r => active.Contains(r.PlayerId) && r.CompletedAt < start).Select(r => r.PlayerId).Distinct().ToListAsync(ct);
        var debuts = active.Except(veterans).Union(registered).ToList();
        return debuts.Select(id =>
            {
                var mine = rows.Where(r => r.PlayerId == id).ToList();
                return new RecapBotDto
                {
                    Name = Name(id),
                    Owner = Owner(id),
                    Registered = registered.Contains(id),
                    Matches = mine.Count,
                    Wins = mine.Count(r => r.Result == MatchResultKind.Win),
                    Losses = mine.Count(r => r.Result == MatchResultKind.Loss),
                    Draws = mine.Count(r => r.Result == MatchResultKind.Draw),
                };
            })
            .OrderByDescending(b => b.Matches).ThenBy(b => b.Name, StringComparer.Ordinal)
            .Take(MaxListed * 2)
            .ToList();
    }

    private async Task<IReadOnlyList<RecapLadderDto>> LaddersAsync(DateOnly day, CancellationToken ct)
    {
        var today = LadderSnapshots.Stored(day);
        var yesterday = LadderSnapshots.Stored(day.AddDays(-1));
        var snapshots = await db.LadderSnapshots.AsNoTracking().Where(s => (s.Date == today || s.Date == yesterday) && s.Rank <= 25).ToListAsync(ct);
        var ladders = new List<RecapLadderDto>();
        foreach (var category in Enum.GetValues<MatchCategory>())
        {
            var live = await PlayerEndpoints.Leaderboard(db, discord, ct, category, limit: 10);
            var now = snapshots.Where(s => s.Date == today && s.Category == category).OrderBy(s => s.Rank).Take(10).ToList();
            var before = snapshots.Where(s => s.Date == yesterday && s.Category == category).ToDictionary(s => s.PlayerId, s => s.Rank);
            var byId = live.ToDictionary(e => e.PlayerId);
            var missing = now.Select(s => s.PlayerId).Where(id => !_players.ContainsKey(id)).ToList();
            foreach (var p in await db.Players.AsNoTracking().Include(p => p.Owner).Where(p => missing.Contains(p.Id)).ToListAsync(ct))
            {
                _players[p.Id] = p;
            }

            foreach (var e in live.Where(e => !_players.ContainsKey(e.PlayerId)))
            {
                _players[e.PlayerId] = new PlayerRecord { Id = e.PlayerId, Name = e.Name, IsHouseBot = e.IsHouseBot };
            }

            // The snapshot for the day if it has been taken, else the ladder as it is now.
            var entries = now.Count > 0
                ? now.Select(s => new RecapLadderEntryDto
                {
                    Rank = s.Rank,
                    PreviousRank = before.TryGetValue(s.PlayerId, out var r) ? r : null,
                    Name = Name(s.PlayerId),
                    Owner = Owner(s.PlayerId),
                    IsHouseBot = _players.GetValueOrDefault(s.PlayerId)?.IsHouseBot ?? false,
                    Rating = s.Rating,
                    Points = s.Points,
                    Wins = byId.GetValueOrDefault(s.PlayerId)?.Wins ?? 0,
                    Losses = byId.GetValueOrDefault(s.PlayerId)?.Losses ?? 0,
                }).ToList()
                : live.Select(e => new RecapLadderEntryDto
                {
                    Rank = e.Rank,
                    PreviousRank = before.TryGetValue(e.PlayerId, out var r) ? r : null,
                    Name = Display(e.Name),
                    Owner = e.OwnerName,
                    IsHouseBot = e.IsHouseBot,
                    Rating = e.Rating,
                    Points = e.Points,
                    Wins = e.Wins,
                    Losses = e.Losses,
                }).ToList();
            if (entries.Any(e => !e.IsHouseBot))
            {
                ladders.Add(new RecapLadderDto { Category = category, RankedBy = category == MatchCategory.Practice ? "rating" : "points", Entries = entries });
            }
        }

        return ladders;
    }

    private IReadOnlyList<RecapMoverDto> Movers(List<MatchPlayerRecord> rows)
    {
        var moves = rows.Where(r => !r.IsHouseBot && r.RatingBefore is not null && r.RatingAfter is not null)
            .GroupBy(r => (r.PlayerId, r.Category))
            .Select(g =>
            {
                var ordered = g.OrderBy(r => r.CompletedAt).ToList();
                return new RecapMoverDto
                {
                    Name = Name(g.Key.PlayerId),
                    Owner = Owner(g.Key.PlayerId),
                    Category = g.Key.Category,
                    From = ordered[0].RatingBefore!.Value,
                    To = ordered[^1].RatingAfter!.Value,
                    Wins = ordered.Count(r => r.Result == MatchResultKind.Win),
                    Losses = ordered.Count(r => r.Result == MatchResultKind.Loss),
                };
            })
            .Where(m => m.To != m.From)
            .ToList();
        return moves.Where(m => m.To > m.From).OrderByDescending(m => m.To - m.From).Take(5)
            .Concat(moves.Where(m => m.To < m.From).OrderBy(m => m.To - m.From).Take(3))
            .ToList();
    }

    private async Task<IReadOnlyList<RecapAchievementDto>> AchievementsAsync(List<BotAchievementRecord> earned, DateTime start, CancellationToken ct)
    {
        var ids = earned.Select(a => a.AchievementId).Distinct().ToList();
        var counts = await db.BotAchievements.Where(a => ids.Contains(a.AchievementId))
            .GroupBy(a => a.AchievementId)
            .Select(g => new { g.Key, Count = g.Count(), Before = g.Count(a => a.EarnedAt < start) })
            .ToDictionaryAsync(x => x.Key, ct);
        return earned
            .Where(a => AchievementCatalog.ById.ContainsKey(a.AchievementId) && _players.TryGetValue(a.PlayerId, out var p) && !p.IsHouseBot)
            .Select(a =>
            {
                var def = AchievementCatalog.ById[a.AchievementId];
                var count = counts.GetValueOrDefault(a.AchievementId);
                return new RecapAchievementDto
                {
                    Bot = Name(a.PlayerId),
                    Owner = Owner(a.PlayerId),
                    Name = def.Name,
                    Description = def.Description,
                    Tier = def.Tier,
                    Hidden = def.Hidden,
                    FirstEver = count?.Before == 0 && earned.Where(e => e.AchievementId == a.AchievementId).MinBy(e => e.EarnedAt) == a,
                    EarnedBy = count?.Count ?? 1,
                };
            })
            // Rarest first, so a long night still leads with the best of it.
            .OrderByDescending(a => a.FirstEver).ThenByDescending(a => a.Tier).ThenBy(a => a.EarnedBy).ThenBy(a => a.Bot, StringComparer.Ordinal)
            .Take(30)
            .ToList();
    }

    private async Task<IReadOnlyList<RecapStrategyDto>> StrategiesAsync(List<MatchPlayerRecord> rows, DateTime start, CancellationToken ct)
    {
        var played = rows.Where(r => !r.IsHouseBot && r.Strategy is not null).ToList();
        var bots = played.Select(r => r.PlayerId).Distinct().ToList();
        var styles = played.Select(r => r.Strategy!).Distinct().ToList();
        var seenBefore = (await db.MatchPlayers.Where(r => r.CompletedAt < start && r.Strategy != null && styles.Contains(r.Strategy))
            .Select(r => r.Strategy!).Distinct().ToListAsync(ct)).ToHashSet();
        var history = (await db.MatchPlayers.AsNoTracking()
                .Where(r => bots.Contains(r.PlayerId) && r.CompletedAt < start && r.Strategy != null)
                .Select(r => new { r.PlayerId, r.Strategy, r.CompletedAt })
                .ToListAsync(ct))
            .GroupBy(r => r.PlayerId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CompletedAt).Take(20).Select(r => r.Strategy!).ToList());

        return played.GroupBy(r => (r.PlayerId, r.Strategy))
            .Select(g =>
            {
                var past = history.GetValueOrDefault(g.Key.PlayerId) ?? [];
                var usual = past.GroupBy(s => s).OrderByDescending(s => s.Count()).ThenBy(s => s.Key, StringComparer.Ordinal).Select(s => s.Key).FirstOrDefault();
                return new RecapStrategyDto
                {
                    Bot = Name(g.Key.PlayerId),
                    Owner = Owner(g.Key.PlayerId),
                    Strategy = Strategies.Names.GetValueOrDefault(g.Key.Strategy!, g.Key.Strategy!),
                    Matches = g.Count(),
                    Wins = g.Count(r => r.Result == MatchResultKind.Win),
                    NewForBot = past.Count > 0 && !past.Contains(g.Key.Strategy!),
                    NewOnServer = !seenBefore.Contains(g.Key.Strategy!),
                    UsedToPlay = usual is not null && usual != g.Key.Strategy ? Strategies.Names.GetValueOrDefault(usual, usual) : null,
                };
            })
            .OrderByDescending(s => s.NewOnServer).ThenByDescending(s => s.NewForBot).ThenByDescending(s => s.Wins).ThenBy(s => s.Bot, StringComparer.Ordinal)
            .Take(MaxListed * 2)
            .ToList();
    }

    /// <summary>The most dramatic real or league match of the day: comebacks, close finishes, upsets, carnage.</summary>
    private RecapMatchDto? MatchOfTheNight(IEnumerable<Guid> candidates, ILookup<Guid, MatchPlayerRecord> byMatch,
        Dictionary<Guid, (MatchResultDto Result, int MaxTicks)> results)
    {
        RecapMatchDto? best = null;
        var bestScore = -1;
        foreach (var id in candidates.Order())
        {
            if (!results.TryGetValue(id, out var match))
            {
                continue;
            }

            var (result, maxTicks) = match;
            var rows = byMatch[id].ToList();
            var why = new List<string>();
            var score = 0;
            var winner = result.Players.FirstOrDefault(p => p.Winner);
            if (winner?.Stats?.LowestScoreSharePercent is int share and <= 60)
            {
                score += 70 - share;
                why.Add($"comeback: the winner fell to {share}% of the leader's score");
            }

            var ranked = result.Players.OrderByDescending(p => p.Score.Total).ToList();
            if (result.Outcome.Reason == MatchEndReason.TimeLimit && ranked.Count > 1 && ranked[0].Score.Total > 0)
            {
                var margin = (ranked[0].Score.Total - ranked[1].Score.Total) * 100.0 / ranked[0].Score.Total;
                if (margin < 5)
                {
                    score += (int)(30 - margin * 5);
                    why.Add($"photo finish: {margin:0.#}% between first and second");
                }
            }

            var winnerRow = rows.FirstOrDefault(r => r.Result == MatchResultKind.Win);
            var favourite = rows.Where(r => r.RatingBefore is not null).MaxBy(r => r.RatingBefore);
            if (winnerRow?.RatingBefore is int wr && favourite?.RatingBefore is int fr && fr - wr >= 50 && favourite != winnerRow)
            {
                score += (fr - wr) / 4;
                why.Add($"upset: {Name(winnerRow.PlayerId)} was rated {fr - wr} below {Name(favourite.PlayerId)}");
            }

            var kills = result.Players.Sum(p => p.UnitsKilled);
            score += Math.Min(30, kills / 3) + result.Players.Count * 2 + (rows.FirstOrDefault()?.Category != MatchCategory.Practice ? 20 : 0);
            if (kills >= 40)
            {
                why.Add($"{kills} units destroyed");
            }

            if (score > bestScore)
            {
                bestScore = score;
                var bySlot = rows.ToDictionary(r => r.Slot);
                best = new RecapMatchDto
                {
                    MatchId = id,
                    Category = rows.First().Category,
                    Reason = result.Outcome.Reason,
                    Ticks = result.Outcome.Ticks,
                    MaxTicks = maxTicks,
                    Why = why,
                    Players = result.Players.OrderByDescending(p => p.Winner).ThenByDescending(p => p.Score.Total).Select(p =>
                    {
                        var row = bySlot.GetValueOrDefault(p.Slot);
                        return new RecapMatchPlayerDto
                        {
                            Name = row is null ? Display(p.Name) : Name(row.PlayerId),
                            Owner = row is null ? null : Owner(row.PlayerId),
                            Slot = p.Slot,
                            Winner = p.Winner,
                            Score = p.Score.Total,
                            UnitsKilled = p.UnitsKilled,
                            UnitsLost = p.UnitsLost,
                            RatingBefore = row?.RatingBefore,
                            Strategy = row?.Strategy is { } s ? Strategies.Names.GetValueOrDefault(s, s) : null,
                        };
                    }).ToList(),
                };
            }
        }

        return best;
    }

    /// <summary>The day's standout single-match performances against real bots.</summary>
    private IReadOnlyList<RecapNoteDto> PowerPlays(List<MatchPlayerRecord> real, Dictionary<Guid, (MatchResultDto Result, int MaxTicks)> results)
    {
        var notes = new List<RecapNoteDto>();
        void Add(MatchPlayerRecord? r, string text)
        {
            if (r is not null && notes.All(n => n.MatchId != r.MatchId || n.Bot != Name(r.PlayerId)))
            {
                notes.Add(new RecapNoteDto { Bot = Name(r.PlayerId), Owner = Owner(r.PlayerId), Text = text, MatchId = r.MatchId });
            }
        }

        var kills = real.MaxBy(r => r.UnitsKilled);
        if (kills is { UnitsKilled: > 0 })
        {
            Add(kills, $"{kills.UnitsKilled} enemy units destroyed in one match");
        }

        var ore = real.MaxBy(r => r.Gathered);
        if (ore is { Gathered: > 0 })
        {
            Add(ore, $"{ore.Gathered:N0} {Lore().Ore} gathered in one match");
        }

        var fastest = real.Where(r => r.Result == MatchResultKind.Win && r.Reason == MatchEndReason.Elimination).MinBy(r => r.Ticks);
        Add(fastest, fastest is null ? "" : $"won by elimination in just {fastest.Ticks} ticks");

        var wrecker = real.Where(r => r.BuildingsDestroyed > 0).MaxBy(r => r.BuildingsDestroyed);
        Add(wrecker, wrecker is null ? "" : $"{wrecker.BuildingsDestroyed} enemy buildings flattened");

        var flawless = real.FirstOrDefault(r => r.Result == MatchResultKind.Win && r.UnitsLost == 0
                                                && results.TryGetValue(r.MatchId, out var m) && m.Result.Players.First(p => p.Slot == r.Slot).BuildingsLost == 0);
        Add(flawless, "a flawless win: not one unit or building lost");

        var bigGain = real.Where(r => r.RatingAfter - r.RatingBefore > 0).MaxBy(r => r.RatingAfter - r.RatingBefore);
        Add(bigGain, bigGain is null ? "" : $"+{bigGain.RatingAfter - bigGain.RatingBefore} rating from a single win");
        return notes.Take(5).ToList();
    }

    private async Task<IReadOnlyList<RecapNoteDto>> SurprisesAsync(List<MatchPlayerRecord> real, ILookup<Guid, MatchPlayerRecord> byMatch,
        Dictionary<Guid, (MatchResultDto Result, int MaxTicks)> results, DateTime start, CancellationToken ct)
    {
        var notes = new List<RecapNoteDto>();
        foreach (var win in real.Where(r => r.Result == MatchResultKind.Win).OrderBy(r => r.CompletedAt))
        {
            var others = byMatch[win.MatchId].Where(o => o.PlayerId != win.PlayerId && !o.IsHouseBot).ToList();
            var favourite = others.Where(o => o.RatingBefore is not null).MaxBy(o => o.RatingBefore);
            if (win.RatingBefore is int mine && favourite?.RatingBefore is int theirs && theirs - mine >= 100)
            {
                notes.Add(new RecapNoteDto
                {
                    Bot = Name(win.PlayerId),
                    Owner = Owner(win.PlayerId),
                    Text = $"upset: beat {Name(favourite.PlayerId)}, rated {theirs - mine} higher",
                    MatchId = win.MatchId,
                });
            }

            if (results.TryGetValue(win.MatchId, out var m) && m.Result.Players.First(p => p.Slot == win.Slot).Stats?.LowestScoreSharePercent is int share and <= 50)
            {
                notes.Add(new RecapNoteDto
                {
                    Bot = Name(win.PlayerId),
                    Owner = Owner(win.PlayerId),
                    Text = $"came back to win after falling to {share}% of the leader's score",
                    MatchId = win.MatchId,
                });
            }
        }

        // Losing streaks snapped, and first ever wins against real bots.
        var winners = real.Where(r => r.Result == MatchResultKind.Win).Select(r => r.PlayerId).Distinct().ToList();
        var before = (await db.MatchPlayers.AsNoTracking()
                .Where(r => winners.Contains(r.PlayerId) && r.CompletedAt < start && r.RealOpponents > 0)
                .Select(r => new { r.PlayerId, r.Result, r.CompletedAt })
                .ToListAsync(ct))
            .GroupBy(r => r.PlayerId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CompletedAt).Select(r => r.Result).ToList());
        foreach (var id in winners)
        {
            var past = before.GetValueOrDefault(id) ?? [];
            var firstWin = real.Where(r => r.PlayerId == id && r.Result == MatchResultKind.Win).MinBy(r => r.CompletedAt)!;
            if (past.Count > 0 && !past.Contains(MatchResultKind.Win))
            {
                notes.Add(new RecapNoteDto { Bot = Name(id), Owner = Owner(id), Text = $"first ever win against a real bot, after {past.Count} tries", MatchId = firstWin.MatchId });
            }
            else
            {
                var streak = past.TakeWhile(r => r == MatchResultKind.Loss).Count();
                if (streak >= 5)
                {
                    notes.Add(new RecapNoteDto { Bot = Name(id), Owner = Owner(id), Text = $"snapped a {streak}-match losing streak", MatchId = firstWin.MatchId });
                }
            }
        }

        // A house bot beating a bot from the top of the practice ladder.
        foreach (var loss in real.Where(r => r.Result == MatchResultKind.Loss && r.RatingBefore >= 1300))
        {
            if (byMatch[loss.MatchId].FirstOrDefault(o => o.IsHouseBot && o.Result == MatchResultKind.Win) is { } house)
            {
                notes.Add(new RecapNoteDto { Bot = Name(house.PlayerId), Text = $"the house bot beat {Name(loss.PlayerId)} (rated {loss.RatingBefore})", MatchId = loss.MatchId });
            }
        }

        return notes.Take(MaxListed).ToList();
    }

    private IReadOnlyList<RecapRivalryDto> Rivalries(List<MatchPlayerRecord> real, ILookup<Guid, MatchPlayerRecord> byMatch)
    {
        var pairs = new Dictionary<(Guid A, Guid B), (int A, int B, int D)>();
        foreach (var match in real.Select(r => r.MatchId).Distinct())
        {
            var bots = byMatch[match].Where(r => !r.IsHouseBot).OrderBy(r => r.PlayerId).ToList();
            for (var i = 0; i < bots.Count; i++)
            {
                for (var j = i + 1; j < bots.Count; j++)
                {
                    if (!MatchHistory.IsRealOpponent(bots[i], bots[j]))
                    {
                        continue;
                    }

                    var key = (bots[i].PlayerId, bots[j].PlayerId);
                    var (a, b, d) = pairs.GetValueOrDefault(key);
                    pairs[key] = bots[i].Result == MatchResultKind.Win ? (a + 1, b, d)
                        : bots[j].Result == MatchResultKind.Win ? (a, b + 1, d)
                        : (a, b, d + 1);
                }
            }
        }

        return pairs.Where(p => p.Value.A + p.Value.B + p.Value.D >= 2)
            .OrderByDescending(p => p.Value.A + p.Value.B + p.Value.D)
            .Take(5)
            .Select(p => new RecapRivalryDto { A = Name(p.Key.A), B = Name(p.Key.B), AWins = p.Value.A, BWins = p.Value.B, Draws = p.Value.D })
            .ToList();
    }

    /// <summary>Bots that played a fair bit, but not in the last week (and not gone for good: seen in the last 60 days).</summary>
    private async Task<IReadOnlyList<RecapMissingDto>> MissingAsync(DateTime end, CancellationToken ct)
    {
        var weekAgo = end.AddDays(-7);
        var longAgo = end.AddDays(-60);
        var gone = await db.MatchPlayers.Where(r => !r.IsHouseBot)
            .GroupBy(r => r.PlayerId)
            .Select(g => new { g.Key, Last = g.Max(r => r.CompletedAt), Count = g.Count() })
            .Where(x => x.Last < weekAgo && x.Last >= longAgo && x.Count >= 5)
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync(ct);
        var ids = gone.Select(g => g.Key).Where(id => !_players.ContainsKey(id)).ToList();
        foreach (var p in await db.Players.AsNoTracking().Include(p => p.Owner).Where(p => ids.Contains(p.Id)).ToListAsync(ct))
        {
            _players[p.Id] = p;
        }

        return gone.Select(g => new RecapMissingDto { Name = Name(g.Key), Owner = Owner(g.Key), DaysAway = (int)(end - g.Last).TotalDays, Matches = g.Count }).ToList();
    }

    private RecapUpcomingDto Upcoming(DateOnly day)
    {
        string? next = null;
        if (recap.Value.LeagueDays.Count > 0)
        {
            var d = day.AddDays(1);
            while (!recap.Value.LeagueDays.Contains(d.DayOfWeek))
            {
                d = d.AddDays(1);
            }

            next = d.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
        }

        return new RecapUpcomingDto
        {
            Schedule = recap.Value.LeagueSchedule,
            NextLeagueDay = next,
            LeagueMatches = leagues.All.Where(l => !l.Started).Select(l => new RecapLeagueMatchDto
            {
                Name = l.Name,
                Category = l.Category,
                Joined = leagues.ToSummary(l).Players.Count,
                Started = l.Started,
            }).ToList(),
        };
    }
}
