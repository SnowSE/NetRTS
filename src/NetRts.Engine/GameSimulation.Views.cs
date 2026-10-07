using NetRts.Protocol;

namespace NetRts.Engine;

public sealed partial class GameSimulation
{
    /// <summary>
    /// What one player may see. Enemy entities outside their vision are omitted, and enemy
    /// orders and production are never revealed.
    /// </summary>
    /// <param name="slot">The viewing player's slot.</param>
    /// <param name="sinceTick">Return events that happened after this tick (default: only the latest tick's).</param>
    public GameStateDto GetPlayerView(int slot, int? sinceTick = null)
    {
        var player = _players[slot];
        var visibility = VisibilityFor(slot);
        var since = sinceTick ?? Tick - 1;

        return new GameStateDto
        {
            MatchId = MatchId,
            Tick = Tick,
            Status = Status,
            MaxTicks = Config.MaxTicks,
            MapWidth = Map.Width,
            MapHeight = Map.Height,
            You = new SelfDto
            {
                PlayerId = player.Id,
                Slot = slot,
                Name = player.Name,
                Resources = player.Resources,
                StorageCapacity = StorageCapacity(slot),
                QueuedCommands = player.CommandQueue.Count,
                QueueCapacity = Config.QueueCapacity,
                IncomePerMinute = player.IncomeWindow.Sum(),
                Upgrades = player.Upgrades.Order().ToList(),
                Researching = _buildings
                    .Where(b => b.Owner == slot && b.Research is not null)
                    .Select(b => ToResearchDto(b))
                    .ToList(),
            },
            Players = _players.Select(p => new PlayerSummaryDto
            {
                PlayerId = p.Id,
                Slot = p.Slot,
                Name = p.Name,
                Eliminated = p.Eliminated,
                Score = ScoreOf(p),
                StartPosition = new PositionDto(Map.StartPositions[p.Slot].X, Map.StartPositions[p.Slot].Y),
            }).ToList(),
            Visibility = VisibilityRows(visibility),
            Units = _units
                .Where(u => u.Owner == slot || IsVisible(visibility, u.Position))
                .Select(u => ToDto(u, revealOrders: u.Owner == slot))
                .ToList(),
            Buildings = _buildings
                .Where(b => b.Owner == slot || IsVisible(visibility, b.Position))
                .Select(b => ToDto(b, revealQueues: b.Owner == slot))
                .ToList(),
            Resources = _deposits
                .Where(d => IsVisible(visibility, d.Position))
                .Select(ToDto)
                .ToList(),
            RememberedBuildings = player.Remembered.Values
                .Where(r => !IsVisible(visibility, new Point(r.X, r.Y)))
                .ToList(),
            Events = player.Events.Where(e => e.Tick > since).ToList(),
            Outcome = Outcome,
        };
    }

    /// <summary>The whole truth, for spectators and replays.</summary>
    public SpectatorStateDto GetSpectatorView(int? sinceTick = null, bool includeVisibility = false)
    {
        var since = sinceTick ?? Tick - 30;
        return new SpectatorStateDto
        {
            MatchId = MatchId,
            Tick = Tick,
            Status = Status,
            MaxTicks = Config.MaxTicks,
            Players = _players.Select(p => new SpectatorPlayerDto
            {
                PlayerId = p.Id,
                Slot = p.Slot,
                Name = p.Name,
                Eliminated = p.Eliminated,
                Resources = p.Resources,
                UnitCount = _units.Count(u => u.Owner == p.Slot),
                BuildingCount = _buildings.Count(b => b.Owner == p.Slot),
                Upgrades = p.Upgrades.Order().ToList(),
                IncomePerMinute = p.IncomeWindow.Sum(),
                Score = ScoreOf(p),
                Visibility = includeVisibility ? VisibilityRows(VisibilityFor(p.Slot)) : [],
            }).ToList(),
            Units = _units.Select(u => ToDto(u, revealOrders: true)).ToList(),
            Buildings = _buildings.Select(b => ToDto(b, revealQueues: true)).ToList(),
            Resources = _deposits.Select(ToDto).ToList(),
            Events = _spectatorEvents.Where(e => e.Tick > since).ToList(),
            Outcome = Outcome,
        };
    }

    public MatchResultDto? GetResult()
    {
        if (Outcome is null)
        {
            return null;
        }

        return new MatchResultDto
        {
            MatchId = MatchId,
            Outcome = Outcome,
            Players = _players.Select(p => new PlayerResultDto
            {
                PlayerId = p.Id,
                Name = p.Name,
                Slot = p.Slot,
                Winner = Outcome.WinnerId == p.Id,
                Score = ScoreOf(p),
                UnitsProduced = p.UnitsProduced,
                UnitsLost = p.UnitsLost,
                UnitsKilled = p.UnitsKilled,
                BuildingsLost = p.BuildingsLost,
                BuildingsDestroyed = p.BuildingsDestroyed,
            }).ToList(),
        };
    }

    private List<string> VisibilityRows(bool[] visibility)
    {
        var rows = new List<string>(Map.Height);
        var row = new char[Map.Width];
        for (var y = 0; y < Map.Height; y++)
        {
            for (var x = 0; x < Map.Width; x++)
            {
                row[x] = visibility[y * Map.Width + x] ? '1' : '0';
            }

            rows.Add(new string(row));
        }

        return rows;
    }

    private static UnitDto ToDto(UnitEntity u, bool revealOrders) => new()
    {
        Id = u.Id,
        Owner = u.Owner,
        Type = u.Type,
        X = u.Position.X,
        Y = u.Position.Y,
        Hp = u.Hp,
        MaxHp = u.MaxHp,
        Activity = u.Activity,
        Carrying = u.Carrying,
        TargetId = revealOrders ? u.TargetId : null,
        Destination = revealOrders && u.Destination is { } d ? new PositionDto(d.X, d.Y) : null,
    };

    private static BuildingDto ToDto(BuildingEntity b, bool revealQueues) => new()
    {
        Id = b.Id,
        Owner = b.Owner,
        Type = b.Type,
        X = b.Position.X,
        Y = b.Position.Y,
        Hp = b.Hp,
        MaxHp = b.MaxHp,
        Completed = b.Completed,
        ConstructionPercent = b.ConstructionPercent,
        Production = revealQueues
            ? b.Production.Select(p => new ProductionDto(p.Type, p.Progress * 100 / GameRules.Units[p.Type].BuildTicks)).ToList()
            : [],
        Research = revealQueues && b.Research is not null ? ToResearchDto(b) : null,
        Rally = revealQueues && b.Rally is { } r ? new PositionDto(r.X, r.Y) : null,
    };

    private static ResearchDto ToResearchDto(BuildingEntity b) =>
        new(b.Research!.Type, b.Research.Progress * 100 / GameRules.Upgrades[b.Research.Type].ResearchTicks, b.Id);

    private static ResourceDepositDto ToDto(DepositEntity d) => new()
    {
        Id = d.Id,
        X = d.Position.X,
        Y = d.Position.Y,
        Remaining = d.Remaining,
    };
}
