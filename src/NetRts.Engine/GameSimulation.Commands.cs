using NetRts.Protocol;

namespace NetRts.Engine;

public sealed partial class GameSimulation
{
    private const int MaxSelectorSpan = 100_000;

    private sealed record ParsedCommand(
        CommandType Type,
        IReadOnlyList<UnitEntity> Units,
        Point? Position = null,
        Entity? Target = null,
        BuildingEntity? Building = null,
        BuildingType? BuildingType = null,
        UnitType? UnitType = null,
        UpgradeType? Upgrade = null,
        int Count = 1);

    private static CommandError Error(string code, string message) => new(code, message);

    /// <summary>
    /// Checks a command against the current state. Used both when it is submitted (strict: every
    /// explicitly listed unit must be yours) and again when it executes (lenient: units that died
    /// in the meantime are simply skipped).
    /// </summary>
    private (ParsedCommand? Command, CommandError? Error) Validate(PlayerState player, CommandRequest request, bool strict, bool[] visibility)
    {
        if (request is null)
        {
            return (null, Error("INVALID_COMMAND", "Command is null."));
        }

        if (!Enum.IsDefined(request.Type))
        {
            return (null, Error("INVALID_COMMAND_TYPE", $"Unknown command type {(int)request.Type}."));
        }

        return request.Type switch
        {
            CommandType.Produce => ValidateProduce(player, request),
            CommandType.Research => ValidateResearch(player, request),
            CommandType.Rally => ValidateRally(player, request),
            _ => ValidateUnitCommand(player, request, strict, visibility),
        };
    }

    private (ParsedCommand?, CommandError?) ValidateUnitCommand(PlayerState player, CommandRequest request, bool strict, bool[] visibility)
    {
        var workersOnly = request.Type is CommandType.Gather or CommandType.Build;
        var (units, selectError) = SelectUnits(player, request, strict, workersOnly);
        if (selectError is not null)
        {
            return (null, selectError);
        }

        var (position, positionError) = ParsePosition(request);
        if (positionError is not null)
        {
            return (null, positionError);
        }

        switch (request.Type)
        {
            case CommandType.Stop:
                return (new ParsedCommand(CommandType.Stop, units), null);

            case CommandType.Move:
                return position is null
                    ? (null, Error("MISSING_POSITION", "Move needs a destination (x/y or tile)."))
                    : (new ParsedCommand(CommandType.Move, units, position), null);

            case CommandType.Attack:
                if (request.TargetId is { } targetId)
                {
                    var target = _entities.GetValueOrDefault(targetId);
                    if (target is DepositEntity || target is null || target.Dead || !IsVisible(visibility, target.Position))
                    {
                        return (null, Error("TARGET_NOT_FOUND", $"No visible unit or building with id {targetId}."));
                    }

                    if (target.Owner == player.Slot)
                    {
                        return (null, Error("FRIENDLY_FIRE", "You cannot attack your own units or buildings."));
                    }

                    return (new ParsedCommand(CommandType.Attack, units, Target: target), null);
                }

                return position is null
                    ? (null, Error("MISSING_TARGET", "Attack needs a targetId, or a position to attack-move to."))
                    : (new ParsedCommand(CommandType.Attack, units, position), null);

            case CommandType.Gather:
            {
                if (request.TargetId is not { } depositId)
                {
                    return (null, Error("MISSING_TARGET", "Gather needs the targetId of a resource deposit."));
                }

                // Deposits are static and neutral, so a bot may send workers to one it saw earlier.
                if (_entities.GetValueOrDefault(depositId) is not DepositEntity deposit)
                {
                    return (null, Error("RESOURCE_NOT_FOUND", $"No resource deposit with id {depositId} (it may be exhausted)."));
                }

                return (new ParsedCommand(CommandType.Gather, units, Target: deposit), null);
            }

            case CommandType.Build:
            {
                if (request.BuildingType is not { } type || !Enum.IsDefined(type))
                {
                    return (null, Error("MISSING_BUILDING_TYPE", "Build needs a valid buildingType."));
                }

                if (position is not { } site)
                {
                    return (null, Error("MISSING_POSITION", "Build needs a site (x/y or tile)."));
                }

                var siteError = CheckBuildSite(player, type, site, visibility, forAssist: true);
                return siteError is not null
                    ? (null, siteError)
                    : (new ParsedCommand(CommandType.Build, units, site, BuildingType: type), null);
            }

            default:
                return (null, Error("INVALID_COMMAND_TYPE", $"Unsupported command type {request.Type}."));
        }
    }

    /// <summary>Checks whether a building of <paramref name="type"/> can go (or is already going) at <paramref name="site"/>.</summary>
    private CommandError? CheckBuildSite(PlayerState player, BuildingType type, Point site, bool[] visibility, bool forAssist)
    {
        if (Map.IsRock(site))
        {
            return Error("INVALID_SITE", $"Tile {site} is rock.");
        }

        if (_depositAt[Index(site)] != 0)
        {
            return Error("POSITION_OCCUPIED", $"Tile {site} holds a resource deposit.");
        }

        var existingId = _buildingAt[Index(site)];
        if (existingId != 0)
        {
            var existing = (BuildingEntity)_entities[existingId];
            if (forAssist && existing.Owner == player.Slot && existing.Type == type && !existing.Completed)
            {
                return null; // help finish our own construction site
            }

            return Error("POSITION_OCCUPIED", $"Tile {site} already has a building.");
        }

        if (_units.Any(u => u.Owner != player.Slot && u.Position == site && IsVisible(visibility, site)))
        {
            return Error("POSITION_OCCUPIED", $"An enemy unit is standing on {site}.");
        }

        var stats = GameRules.Buildings[type];
        foreach (var required in stats.Requires)
        {
            if (!_buildings.Any(b => b.Owner == player.Slot && b.Type == required && b.Completed))
            {
                return Error("MISSING_PREREQUISITE", $"{type} requires a completed {required}.");
            }
        }

        if (player.Resources < stats.Cost)
        {
            return Error("INSUFFICIENT_RESOURCES", $"{type} costs {stats.Cost}; you have {player.Resources}.");
        }

        return null;
    }

    private (ParsedCommand?, CommandError?) ValidateProduce(PlayerState player, CommandRequest request)
    {
        var (building, error) = OwnCompletedBuilding(player, request);
        if (error is not null)
        {
            return (null, error);
        }

        if (request.UnitType is not { } unitType || !Enum.IsDefined(unitType))
        {
            return (null, Error("MISSING_UNIT_TYPE", "Produce needs a valid unitType."));
        }

        if (!building!.Stats.Produces.Contains(unitType))
        {
            return (null, Error("CANNOT_PRODUCE", $"{building.Type} cannot produce {unitType}."));
        }

        var count = request.Count ?? 1;
        if (count < 1)
        {
            return (null, Error("INVALID_COUNT", "count must be at least 1."));
        }

        if (building.Production.Count + count > GameRules.MaxProductionQueue)
        {
            return (null, Error("PRODUCTION_QUEUE_FULL",
                $"Building {building.Id} has {building.Production.Count} queued; the limit is {GameRules.MaxProductionQueue}."));
        }

        var cost = GameRules.Units[unitType].Cost * count;
        if (player.Resources < cost)
        {
            return (null, Error("INSUFFICIENT_RESOURCES", $"{count} x {unitType} costs {cost}; you have {player.Resources}."));
        }

        var (rally, rallyError) = ParsePosition(request);
        if (rallyError is not null)
        {
            return (null, rallyError);
        }

        return (new ParsedCommand(CommandType.Produce, [], rally, Building: building, UnitType: unitType, Count: count), null);
    }

    private (ParsedCommand?, CommandError?) ValidateRally(PlayerState player, CommandRequest request)
    {
        var (building, error) = OwnCompletedBuilding(player, request);
        if (error is not null)
        {
            return (null, error);
        }

        if (building!.Stats.Produces.Count == 0)
        {
            return (null, Error("CANNOT_RALLY", $"{building.Type} doesn't train units, so it has no rally point."));
        }

        var (rally, rallyError) = ParsePosition(request);
        if (rallyError is not null)
        {
            return (null, rallyError);
        }

        return rally is null
            ? (null, Error("MISSING_POSITION", "Rally needs a position (x/y or tile)."))
            : (new ParsedCommand(CommandType.Rally, [], rally, Building: building), null);
    }

    private (ParsedCommand?, CommandError?) ValidateResearch(PlayerState player, CommandRequest request)
    {
        var (building, error) = OwnCompletedBuilding(player, request);
        if (error is not null)
        {
            return (null, error);
        }

        if (!building!.Stats.CanResearch)
        {
            return (null, Error("CANNOT_RESEARCH", $"{building.Type} cannot research; use a TechLab."));
        }

        if (request.Upgrade is not { } upgrade || !Enum.IsDefined(upgrade))
        {
            return (null, Error("MISSING_UPGRADE", "Research needs a valid upgrade."));
        }

        if (player.Upgrades.Contains(upgrade))
        {
            return (null, Error("ALREADY_RESEARCHED", $"{upgrade} is already researched."));
        }

        if (_buildings.Any(b => b.Owner == player.Slot && b.Research?.Type == upgrade))
        {
            return (null, Error("ALREADY_RESEARCHING", $"{upgrade} is already being researched."));
        }

        if (building.Research is not null)
        {
            return (null, Error("BUILDING_BUSY", $"TechLab {building.Id} is already researching {building.Research.Type}."));
        }

        var stats = GameRules.Upgrades[upgrade];
        if (stats.Requires is { } prerequisite && !player.Upgrades.Contains(prerequisite))
        {
            return (null, Error("MISSING_PREREQUISITE", $"{upgrade} requires {prerequisite}."));
        }

        if (player.Resources < stats.Cost)
        {
            return (null, Error("INSUFFICIENT_RESOURCES", $"{upgrade} costs {stats.Cost}; you have {player.Resources}."));
        }

        return (new ParsedCommand(CommandType.Research, [], Building: building, Upgrade: upgrade), null);
    }

    private (BuildingEntity?, CommandError?) OwnCompletedBuilding(PlayerState player, CommandRequest request)
    {
        if (request.BuildingId is not { } id)
        {
            return (null, Error("MISSING_BUILDING", $"{request.Type} needs a buildingId."));
        }

        if (_entities.GetValueOrDefault(id) is not BuildingEntity building || building.Owner != player.Slot)
        {
            return (null, Error("BUILDING_NOT_FOUND", $"You have no building with id {id}."));
        }

        if (!building.Completed)
        {
            return (null, Error("BUILDING_NOT_OPERATIONAL", $"Building {id} is still under construction."));
        }

        return (building, null);
    }

    private (IReadOnlyList<UnitEntity>, CommandError?) SelectUnits(PlayerState player, CommandRequest request, bool strict, bool workersOnly)
    {
        if (string.IsNullOrWhiteSpace(request.Units) && (request.UnitIds is null || request.UnitIds.Length == 0))
        {
            return ([], Error("NO_UNITS_SPECIFIED", "Select units with 'units' (e.g. \"1-7,12\", \"all\", \"idle\") and/or 'unitIds'."));
        }

        var selected = new HashSet<int>();

        foreach (var id in request.UnitIds ?? [])
        {
            if (_entities.GetValueOrDefault(id) is UnitEntity unit && unit.Owner == player.Slot)
            {
                selected.Add(id);
            }
            else if (strict)
            {
                return ([], Error("UNIT_NOT_OWNED", $"Unit {id} does not exist or is not yours."));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Units))
        {
            var selector = request.Units.Trim();
            if (selector.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                selected.UnionWith(_units.Where(u => u.Owner == player.Slot).Select(u => u.Id));
            }
            else if (selector.Equals("idle", StringComparison.OrdinalIgnoreCase))
            {
                selected.UnionWith(_units.Where(u => u.Owner == player.Slot && u.Order == OrderKind.Idle).Select(u => u.Id));
            }
            else
            {
                var ranges = ParseSelector(selector);
                if (ranges is null)
                {
                    return ([], Error("INVALID_SELECTOR", $"Could not parse units selector \"{selector}\". Use ids and ranges like \"1-7,12\", or \"all\"/\"idle\"."));
                }

                selected.UnionWith(_units
                    .Where(u => u.Owner == player.Slot && ranges.Any(r => u.Id >= r.From && u.Id <= r.To))
                    .Select(u => u.Id));
            }
        }

        var units = _units.Where(u => selected.Contains(u.Id)
                                      && (request.UnitType is null || u.Type == request.UnitType)
                                      && (!workersOnly || u.Type == UnitType.Worker))
                          .ToList();

        if (units.Count == 0)
        {
            var what = workersOnly ? "workers" : "units";
            return ([], Error("NO_MATCHING_UNITS", $"The selection matched none of your {what}."));
        }

        return (units, null);
    }

    internal static List<(int From, int To)>? ParseSelector(string selector)
    {
        var ranges = new List<(int, int)>();
        foreach (var raw in selector.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = raw.IndexOf('-', 1);
            if (dash < 0)
            {
                if (!int.TryParse(raw, out var single))
                {
                    return null;
                }

                ranges.Add((single, single));
                continue;
            }

            if (!int.TryParse(raw[..dash].Trim(), out var from) || !int.TryParse(raw[(dash + 1)..].Trim(), out var to)
                || to < from || (long)to - from > MaxSelectorSpan)
            {
                return null;
            }

            ranges.Add((from, to));
        }

        return ranges.Count == 0 ? null : ranges;
    }

    private (Point?, CommandError?) ParsePosition(CommandRequest request)
    {
        if (request.Tile is { } tile)
        {
            if (tile < 0 || tile >= Map.Width * Map.Height)
            {
                return (null, Error("OUT_OF_BOUNDS", $"Tile {tile} is outside the {Map.Width}x{Map.Height} map."));
            }

            return (new Point(tile % Map.Width, tile / Map.Width), null);
        }

        if (request.X is null && request.Y is null)
        {
            return (null, null);
        }

        if (request.X is not { } x || request.Y is not { } y)
        {
            return (null, Error("MISSING_POSITION", "Provide both x and y."));
        }

        if (!Map.InBounds(x, y))
        {
            return (null, Error("OUT_OF_BOUNDS", $"({x},{y}) is outside the {Map.Width}x{Map.Height} map."));
        }

        return (new Point(x, y), null);
    }

    private void ExecuteQueuedCommands()
    {
        foreach (var player in _players.Where(p => !p.Eliminated))
        {
            var visibility = VisibilityFor(player.Slot);
            for (var n = 0; n < Config.CommandsPerTick && player.CommandQueue.Count > 0; n++)
            {
                var queued = player.CommandQueue.Dequeue();
                var (command, error) = Validate(player, queued.Request, strict: false, visibility);
                if (error is not null)
                {
                    AddEvent(player, "CommandFailed", $"{queued.Request.Type} failed: {error.Message}", commandId: queued.Id, spectator: false);
                    continue;
                }

                Apply(player, command!);
                foreach (var unit in command!.Units)
                {
                    unit.OrderCommandId = queued.Id;
                }
            }
        }
    }

    // Later commands for the same unit simply overwrite its order, so the most recent one wins.
    private void Apply(PlayerState player, ParsedCommand command)
    {
        switch (command.Type)
        {
            case CommandType.Stop:
                foreach (var u in command.Units)
                {
                    u.ClearOrder();
                }

                break;

            case CommandType.Move:
                foreach (var u in command.Units)
                {
                    u.ClearOrder();
                    u.Order = OrderKind.Move;
                    u.Destination = command.Position;
                    u.Activity = UnitActivity.Moving;
                }

                break;

            case CommandType.Attack:
                foreach (var u in command.Units)
                {
                    u.ClearOrder();
                    if (command.Target is not null)
                    {
                        u.Order = OrderKind.AttackTarget;
                        u.TargetId = command.Target.Id;
                    }
                    else
                    {
                        u.Order = OrderKind.AttackMove;
                        u.Destination = command.Position;
                    }

                    u.Activity = UnitActivity.Moving;
                }

                break;

            case CommandType.Gather:
                foreach (var u in command.Units)
                {
                    u.ClearOrder();
                    u.Order = OrderKind.Gather;
                    u.TargetId = command.Target!.Id;
                    u.LastDepositPosition = command.Target.Position;
                    u.Activity = UnitActivity.Moving;
                }

                break;

            case CommandType.Build:
                foreach (var u in command.Units)
                {
                    u.ClearOrder();
                    u.Order = OrderKind.Build;
                    u.BuildType = command.BuildingType;
                    u.Destination = command.Position;
                    u.Activity = UnitActivity.Moving;
                }

                break;

            case CommandType.Produce:
                player.Resources -= GameRules.Units[command.UnitType!.Value].Cost * command.Count;
                for (var i = 0; i < command.Count; i++)
                {
                    command.Building!.Production.Add(new ProductionItem(command.UnitType.Value));
                }

                if (command.Position is { } produceRally)
                {
                    command.Building!.Rally = produceRally;
                }

                break;

            case CommandType.Rally:
                command.Building!.Rally = command.Position;
                break;

            case CommandType.Research:
                player.Resources -= GameRules.Upgrades[command.Upgrade!.Value].Cost;
                command.Building!.Research = new ResearchItem(command.Upgrade.Value);
                break;
        }
    }
}
