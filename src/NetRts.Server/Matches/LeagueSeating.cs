using NetRts.Engine;

namespace NetRts.Server.Matches;

/// <summary>
/// Seats a round of a league match. Everyone is shuffled into heats at random; if that puts two bots
/// next to each other who were already neighbours in an earlier round, it shuffles again, and after
/// enough tries keeps the seating with the fewest repeats (small heats can't always avoid them).
/// </summary>
public static class LeagueSeating
{
    public const int Attempts = 500;

    /// <summary>The bots in each heat, in seat order (slot 0 first), and how many neighbour pairs repeat.</summary>
    public static (List<List<Seat>> Heats, int Repeats) Plan(
        IReadOnlyList<Seat> players, IReadOnlyList<int> sizes, IReadOnlySet<(Guid, Guid)> previousNeighbours, Random random)
    {
        List<List<Seat>>? best = null;
        var fewest = int.MaxValue;
        for (var attempt = 0; attempt < Attempts && fewest > 0; attempt++)
        {
            var order = players.ToArray();
            random.Shuffle(order);
            var heats = new List<List<Seat>>();
            var taken = 0;
            foreach (var size in sizes)
            {
                heats.Add([.. order.Skip(taken).Take(size)]);
                taken += size;
            }

            var repeats = heats.Sum(h => Neighbours(h.Select(s => s.PlayerId).ToList()).Count(previousNeighbours.Contains));
            if (repeats < fewest)
            {
                (best, fewest) = (heats, repeats);
            }
        }

        return (best ?? [], fewest == int.MaxValue ? 0 : fewest);
    }

    /// <summary>The pairs of players who start next to each other, given a heat's players in seat order.</summary>
    public static IEnumerable<(Guid, Guid)> Neighbours(IReadOnlyList<Guid> seatOrder) =>
        MapGenerator.NeighbourSlots(seatOrder.Count).Select(p => Pair(seatOrder[p.A], seatOrder[p.B]));

    private static (Guid, Guid) Pair(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);
}
