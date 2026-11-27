using System.Globalization;
using System.Text;
using NetRts.Protocol;

namespace NetRts.Sim;

/// <summary>Markdown tables comparing the variants: how games end, how long they last, and who wins.</summary>
internal static class Report
{
    public static string Render(Options options, IReadOnlyList<Game> games)
    {
        var text = new StringBuilder();
        var byVariant = options.Variants.Select(v => (Name: v, Games: games.Where(g => g.Variant == v).ToList())).ToList();

        text.AppendLine(CultureInfo.InvariantCulture, $"{options.Players}-player games, {options.Seeds} seeds, tick limit {options.Ticks}, bots: {string.Join(", ", options.Bots)}");
        text.AppendLine();
        text.AppendLine("| variant | games | eliminated | tick limit | mean end | median end | leader at 300 wins | tick-limit flips w/o economy | economy share of score | tower kill share | towers / player | trebuchets / game | spoils / player | HQs / player | gathered / player | farms / player |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var (name, list) in byVariant)
        {
            var seats = list.SelectMany(g => g.Seats).ToList();
            var timeLimit = list.Where(g => g.Reason == MatchEndReason.TimeLimit && g.Winner is not null).ToList();
            var decided = list.Where(g => g.Winner is not null && g.LeaderAt300 is not null).ToList();
            var score = seats.Sum(s => (long)s.Score.Total);
            text.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {list.Count} " +
                $"| {Pct(list.Count(g => g.Reason == MatchEndReason.Elimination), list.Count)} " +
                $"| {Pct(list.Count(g => g.Reason == MatchEndReason.TimeLimit), list.Count)} " +
                $"| {Avg(list.Select(g => g.Ticks), "0")} " +
                $"| {Median(list.Select(g => g.Ticks))} " +
                $"| {Pct(decided.Count(g => g.Winner == g.LeaderAt300), decided.Count)} " +
                $"| {Pct(timeLimit.Count(g => g.Seats.MaxBy(s => s.Score.Destruction + s.Score.Survival)!.Slot != g.Winner), timeLimit.Count)} " +
                $"| {Pct(seats.Sum(s => (long)s.Score.Economy), score)} " +
                $"| {Pct(seats.Sum(s => s.Stats.TowerKills), seats.Sum(s => s.UnitsKilled))} " +
                $"| {Avg(seats.Select(s => Built(s, BuildingType.GuardTower)))} " +
                $"| {Avg(list.Select(g => g.Seats.Sum(s => Trained(s, UnitType.Trebuchet))))} " +
                $"| {Avg(seats.Select(s => s.Stats.Spoils))} " +
                $"| {Avg(seats.Select(s => Built(s, BuildingType.CommandCenter)))} " +
                $"| {Avg(seats.Select(s => s.Score.Economy), "0")} " +
                $"| {Avg(seats.Select(s => Built(s, BuildingType.Farm)))} |");
        }

        text.AppendLine();
        text.AppendLine("Win rate by bot (draws and no-winner games count as losses):");
        text.AppendLine();
        text.AppendLine("| variant | " + string.Join(" | ", options.Bots) + " |");
        text.AppendLine("|---|" + string.Concat(options.Bots.Select(_ => "---|")));
        foreach (var (name, list) in byVariant)
        {
            var cells = options.Bots.Select(bot =>
            {
                var played = list.SelectMany(g => g.Seats.Select(s => (Game: g, s.Slot))).Where(x => x.Game.Bots[x.Slot] == bot).ToList();
                return Pct(played.Count(x => x.Game.Winner == x.Slot), played.Count);
            });
            text.AppendLine(CultureInfo.InvariantCulture, $"| {name} | {string.Join(" | ", cells)} |");
        }

        return text.ToString();
    }

    private static int Built(Seat seat, BuildingType type) => seat.Stats.BuildingsCompleted.GetValueOrDefault(type);

    private static int Trained(Seat seat, UnitType type) => seat.Stats.UnitsTrained.GetValueOrDefault(type);

    private static string Pct(long part, long whole) => whole == 0 ? "-" : (100.0 * part / whole).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string Avg(IEnumerable<int> values, string format = "0.0")
    {
        var list = values.ToList();
        return list.Count == 0 ? "-" : list.Average().ToString(format, CultureInfo.InvariantCulture);
    }

    private static string Median(IEnumerable<int> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? "-" : sorted[sorted.Count / 2].ToString(CultureInfo.InvariantCulture);
    }
}
