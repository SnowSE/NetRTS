using NetRts.Protocol;
using NetRts.Server.Data;

namespace NetRts.Server.Achievements;

/// <summary>What the history needs to know about a seat in a finished match.</summary>
public sealed record HistorySeat(Guid PlayerId, int Slot, bool IsHouseBot, Guid? OwnerAccountId);

/// <summary>Builds the <see cref="MatchPlayerRecord"/> rows for a finished match.</summary>
public static class MatchHistory
{
    /// <summary>
    /// Whether <paramref name="other"/> counts as a real opponent for <paramref name="me"/>: a bot
    /// that isn't a house bot and isn't owned by the same person.
    /// </summary>
    public static bool IsRealOpponent(MatchPlayerRecord me, MatchPlayerRecord other) =>
        other.PlayerId != me.PlayerId && !other.IsHouseBot && !(me.OwnerAccountId is { } owner && owner == other.OwnerAccountId);

    /// <summary>
    /// One row per registered bot in the match (<paramref name="known"/>: second and later copies of a
    /// house bot aren't registered).
    /// </summary>
    public static List<MatchPlayerRecord> Rows(
        Guid matchId,
        DateTime completedAt,
        MatchCategory category,
        MatchResultDto result,
        IReadOnlyList<HistorySeat> seats,
        IReadOnlySet<Guid> known,
        IReadOnlyDictionary<Guid, (int Before, int After)>? ratings = null,
        IReadOnlySet<int>? surrenderedSlots = null)
    {
        var players = result.Players;
        var places = players.ToDictionary(p => p.Slot, p => 1 + players.Count(q =>
            (q.Winner && !p.Winner) || (q.Winner == p.Winner && q.Score.Total > p.Score.Total)));
        var bySlot = seats.ToDictionary(s => s.Slot);
        var rows = new List<MatchPlayerRecord>();
        foreach (var p in players)
        {
            if (!bySlot.TryGetValue(p.Slot, out var seat) || !known.Contains(seat.PlayerId) || rows.Any(r => r.PlayerId == seat.PlayerId))
            {
                continue;
            }

            (int Before, int After)? rating = ratings is not null && ratings.TryGetValue(seat.PlayerId, out var r) ? r : null;
            rows.Add(new MatchPlayerRecord
            {
                MatchId = matchId,
                PlayerId = seat.PlayerId,
                Slot = p.Slot,
                CompletedAt = completedAt,
                Category = category,
                IsHouseBot = seat.IsHouseBot,
                OwnerAccountId = seat.OwnerAccountId,
                Result = result.Outcome.WinnerId is null ? MatchResultKind.Draw : p.Winner ? MatchResultKind.Win : MatchResultKind.Loss,
                Place = places[p.Slot],
                PlayerCount = players.Count,
                Ticks = result.Outcome.Ticks,
                Reason = result.Outcome.Reason,
                Score = p.Score.Total,
                Gathered = p.Score.Economy,
                UnitsKilled = p.UnitsKilled,
                UnitsLost = p.UnitsLost,
                BuildingsDestroyed = p.BuildingsDestroyed,
                Surrendered = surrenderedSlots?.Contains(p.Slot) ?? false,
                RatingBefore = rating?.Before,
                RatingAfter = rating?.After,
                Strategy = Strategies.Classify(p.Stats),
            });
        }

        // Every seat counts as an opponent, registered or not (extra house bot copies are never real).
        foreach (var row in rows)
        {
            row.RealOpponents = seats.Count(s => s.Slot != row.Slot && !s.IsHouseBot && s.PlayerId != row.PlayerId
                                                 && !(row.OwnerAccountId is { } owner && owner == s.OwnerAccountId));
        }

        return rows;
    }
}
