using NetRts.Domain.ValueObjects;

namespace NetRts.Domain.Entities;

/// <summary>
/// Represents a gatherable resource location (ore, crystals).
/// </summary>
public class ResourceDeposit
{
    public int Id { get; private set; }
    public Guid MatchId { get; private set; }
    public Position Position { get; private set; } = new(0, 0);
    public string ResourceType { get; private set; } = "Ore";
    public int RemainingCapacity { get; private set; }
    public int InitialCapacity { get; private set; }
    public int GatherRate { get; private set; }

    private ResourceDeposit() { }

    public ResourceDeposit(int id, Guid matchId, Position position, int initialCapacity = 5000, int gatherRate = 10)
    {
        Id = id;
        MatchId = matchId;
        Position = position;
        InitialCapacity = initialCapacity;
        RemainingCapacity = initialCapacity;
        GatherRate = gatherRate;
    }

    public int Gather(int amount)
    {
        var gathered = Math.Min(amount, RemainingCapacity);
        RemainingCapacity -= gathered;
        return gathered;
    }

    public bool IsDepleted() => RemainingCapacity <= 0;
}
