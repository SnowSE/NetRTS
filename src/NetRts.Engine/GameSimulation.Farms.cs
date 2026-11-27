using NetRts.Protocol;

namespace NetRts.Engine;

/// <summary>
/// Farms (<see cref="RuleSet.Farm"/>): a finished farm holds a crop of grubs that its owner's workers dig like
/// a patch. Once it's dug out it stays standing, empty, until a worker sent to Gather it replants it.
/// </summary>
public sealed partial class GameSimulation
{
    /// <summary>A building just finished: a farm gets its first crop.</summary>
    private void Planted(BuildingEntity building)
    {
        if (building.Type == BuildingType.Farm && Rules.Farm is { } farm)
        {
            building.Grubs = farm.Grubs;
        }
    }

    /// <summary>The last grub is dug: the farm's workers move on, as from a patch that ran dry.</summary>
    private void FarmEmptied(PlayerState player, BuildingEntity farm)
    {
        foreach (var unit in _units.Where(u => u.Order == OrderKind.Gather && u.TargetId == farm.Id))
        {
            unit.TargetId = null;
            unit.GatherSpot = null;
        }

        AddEvent(player, "FarmEmpty", $"Farm {farm.Id} at {farm.Position} is dug out; Gather it to replant", farm.Id);
    }

    /// <summary>A worker sent to an empty farm replants it, if its owner can pay.</summary>
    private void Replant(UnitEntity unit, PlayerState player, BuildingEntity farm)
    {
        var cost = Rules.Farm!.ReplantCost;
        if (player.Resources < cost)
        {
            AddEvent(player, "CommandFailed", $"Replanting farm {farm.Id} costs {cost}; you have {player.Resources}", unit.Id, unit.OrderCommandId, spectator: false);
            unit.ClearOrder();
            return;
        }

        player.Resources -= cost;
        farm.Grubs = Rules.Farm.Grubs;
        unit.Activity = UnitActivity.Gathering;
        AddEvent(player, "FarmReplanted", $"Farm {farm.Id} at {farm.Position} replanted", farm.Id);
    }

    /// <summary>Checks a Gather aimed at a building: only your own finished farm, and replanting one needs the grubs.</summary>
    private (ParsedCommand?, CommandError?) ValidateFarmGather(PlayerState player, IReadOnlyList<UnitEntity> units, BuildingEntity building, bool strict)
    {
        if (building.Type != BuildingType.Farm || building.Owner != player.Slot)
        {
            return (null, Error("RESOURCE_NOT_FOUND", $"Building {building.Id} isn't one of your farms."));
        }

        if (!building.Completed)
        {
            return (null, Error("BUILDING_NOT_OPERATIONAL", $"Farm {building.Id} is still under construction."));
        }

        if (strict && building.Grubs == 0 && player.Resources < Rules.Farm!.ReplantCost)
        {
            return (null, Error("INSUFFICIENT_RESOURCES", $"Replanting farm {building.Id} costs {Rules.Farm.ReplantCost}; you have {player.Resources}."));
        }

        return (new ParsedCommand(CommandType.Gather, units, Target: building), null);
    }
}
