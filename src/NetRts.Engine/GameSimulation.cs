using NetRts.Protocol;

namespace NetRts.Engine;

public sealed class GameConfig
{
    public int MapWidth { get; init; } = 64;
    public int MapHeight { get; init; } = 64;
    public int Seed { get; init; }

    /// <summary>Tick limit; at this tick the match ends on score. 1800 = 30 minutes at 1 tick/second.</summary>
    public int MaxTicks { get; init; } = 1800;

    /// <summary>Maximum commands waiting in one player's queue.</summary>
    public int QueueCapacity { get; init; } = 500;

    /// <summary>Commands taken from the front of each player's queue per tick.</summary>
    public int CommandsPerTick { get; init; } = 100;

    /// <summary>Maximum commands accepted in a single submission.</summary>
    public int MaxCommandsPerSubmit { get; init; } = 100;

    /// <summary>How many recent events are kept per player.</summary>
    public int EventHistory { get; init; } = 200;
}

public sealed record PlayerSeat(Guid Id, string Name);

/// <summary>
/// One match. Pure and deterministic: given the same config, seats and command stream it always
/// produces the same result. Not thread safe — the host must serialise access.
/// </summary>
public sealed partial class GameSimulation
{
    private readonly List<PlayerState> _players = [];
    private readonly List<UnitEntity> _units = [];
    private readonly List<BuildingEntity> _buildings = [];
    private readonly List<DepositEntity> _deposits = [];
    private readonly Dictionary<int, Entity> _entities = [];
    private readonly int[] _buildingAt;
    private readonly int[] _depositAt;
    private readonly List<GameEventDto> _spectatorEvents = [];
    private readonly List<ReplayCommandDto> _replayCommands = [];
    private readonly List<ReplaySurrenderDto> _replaySurrenders = [];
    private bool[][]? _visibility;
    private int _nextEntityId = 1;
    private long _nextCommandId = 1;

    public GameSimulation(Guid matchId, GameConfig config, IReadOnlyList<PlayerSeat> seats)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(seats);

        MatchId = matchId;
        Config = config;
        Map = MapGenerator.Generate(config.MapWidth, config.MapHeight, config.Seed, seats.Count);
        _buildingAt = new int[Map.Width * Map.Height];
        _depositAt = new int[Map.Width * Map.Height];

        foreach (var spawn in Map.Deposits)
        {
            var deposit = new DepositEntity(_nextEntityId++, spawn.Position, spawn.Amount);
            _deposits.Add(deposit);
            _entities[deposit.Id] = deposit;
            _depositAt[Index(spawn.Position)] = deposit.Id;
        }

        for (var slot = 0; slot < seats.Count; slot++)
        {
            _players.Add(new PlayerState(seats[slot].Id, slot, seats[slot].Name));
            var start = Map.StartPositions[slot];
            AddBuilding(slot, BuildingType.CommandCenter, start, completed: true);
            for (var i = 0; i < GameRules.StartingWorkers; i++)
            {
                var spot = FindFreeTileNear(start) ?? start;
                AddUnit(slot, UnitType.Worker, spot);
            }
        }
    }

    public Guid MatchId { get; }
    public GameConfig Config { get; }
    public GameMap Map { get; }
    public int Tick { get; private set; }
    public MatchStatus Status { get; private set; } = MatchStatus.Active;
    public MatchOutcomeDto? Outcome { get; private set; }
    public int PlayerCount => _players.Count;

    public int SlotOf(Guid playerId) => _players.FindIndex(p => p.Id == playerId);

    public int QueueSize(int slot) => _players[slot].CommandQueue.Count;

    /// <summary>Validates and queues commands for execution on upcoming ticks.</summary>
    public SubmitCommandsResponse Submit(int slot, IReadOnlyList<CommandRequest> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var player = _players[slot];
        var results = new List<CommandResult>(commands.Count);

        CommandError? blanket = Status != MatchStatus.Active
            ? new("MATCH_NOT_ACTIVE", "The match is not in progress.")
            : player.Eliminated
                ? new("PLAYER_ELIMINATED", "You have been eliminated.")
                : commands.Count > Config.MaxCommandsPerSubmit
                    ? new("TOO_MANY_COMMANDS", $"At most {Config.MaxCommandsPerSubmit} commands per request.")
                    : null;

        var visibility = blanket is null ? VisibilityFor(slot) : null;
        for (var i = 0; i < commands.Count; i++)
        {
            var error = blanket;
            if (error is null && player.CommandQueue.Count >= Config.QueueCapacity)
            {
                error = new("QUEUE_FULL", $"Your command queue is full ({Config.QueueCapacity}).");
            }

            if (error is null)
            {
                (_, error) = Validate(player, commands[i], strict: true, visibility!);
            }

            if (error is not null)
            {
                results.Add(new CommandResult { Index = i, Accepted = false, Error = error });
                continue;
            }

            var id = Enqueue(slot, commands[i]);
            results.Add(new CommandResult { Index = i, Accepted = true, CommandId = id });
        }

        return new SubmitCommandsResponse
        {
            Accepted = results.Count(r => r.Accepted),
            Rejected = results.Count(r => !r.Accepted),
            Results = results,
            QueueSize = player.CommandQueue.Count,
            QueueCapacity = Config.QueueCapacity,
        };
    }

    /// <summary>Drops every command still waiting in the player's queue. Returns how many were dropped.</summary>
    public int ClearQueue(int slot)
    {
        var count = _players[slot].CommandQueue.Count;
        _players[slot].CommandQueue.Clear();
        return count;
    }

    public void Surrender(int slot)
    {
        if (Status != MatchStatus.Active || _players[slot].Eliminated)
        {
            return;
        }

        _replaySurrenders.Add(new ReplaySurrenderDto(Tick, slot));
        Eliminate(_players[slot], "surrendered");
        CheckForEnd(MatchEndReason.Surrender);
        _visibility = null;
    }

    /// <summary>Re-simulates a recorded match from its replay.</summary>
    public static GameSimulation FromReplay(ReplayDto replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        var config = new GameConfig
        {
            MapWidth = replay.MapWidth,
            MapHeight = replay.MapHeight,
            Seed = replay.Seed,
            MaxTicks = replay.MaxTicks,
            QueueCapacity = int.MaxValue,
        };
        var sim = new GameSimulation(replay.MatchId, config, replay.Players.Select((id, i) => new PlayerSeat(id, $"P{i}")).ToList());

        var commandsByTick = replay.Commands.ToLookup(c => c.Tick);
        var surrendersByTick = replay.Surrenders.ToLookup(s => s.Tick);
        while (sim.Status == MatchStatus.Active)
        {
            foreach (var c in commandsByTick[sim.Tick])
            {
                sim.Enqueue(c.Slot, c.Command);
            }

            foreach (var s in surrendersByTick[sim.Tick])
            {
                sim.Surrender(s.Slot);
            }

            sim.Step();
        }

        return sim;
    }

    public ReplayDto GetReplay() => new()
    {
        MatchId = MatchId,
        MapWidth = Config.MapWidth,
        MapHeight = Config.MapHeight,
        Seed = Config.Seed,
        MaxTicks = Config.MaxTicks,
        Players = _players.Select(p => p.Id).ToList(),
        Commands = _replayCommands.ToList(),
        Surrenders = _replaySurrenders.ToList(),
        Outcome = Outcome,
    };

    /// <summary>A fingerprint of the full simulation state, for determinism checks.</summary>
    public string StateHash() =>
        string.Join('|',
            Tick,
            string.Join(',', _players.Select(p => $"{p.Resources}:{p.Destruction}:{p.Gathered}:{p.Eliminated}")),
            string.Join(',', _units.Select(u => $"{u.Id}:{u.Position.X}:{u.Position.Y}:{u.Hp}:{u.Carrying}:{(int)u.Order}")),
            string.Join(',', _buildings.Select(b => $"{b.Id}:{b.Hp}:{b.Work}:{b.Rally}")),
            string.Join(',', _deposits.Select(d => $"{d.Id}:{d.Hp}")));

    private long Enqueue(int slot, CommandRequest request)
    {
        var id = _nextCommandId++;
        _players[slot].CommandQueue.Enqueue(new QueuedCommand(id, request));
        _replayCommands.Add(new ReplayCommandDto(Tick, slot, request));
        return id;
    }

    private int Index(Point p) => p.Y * Map.Width + p.X;

    private bool Passable(int x, int y)
    {
        if (!Map.InBounds(x, y) || Map.IsRock(x, y))
        {
            return false;
        }

        var i = y * Map.Width + x;
        return _buildingAt[i] == 0 && _depositAt[i] == 0;
    }

    private bool Passable(Point p) => Passable(p.X, p.Y);

    private UnitEntity AddUnit(int owner, UnitType type, Point position)
    {
        var unit = new UnitEntity(_nextEntityId++, owner, type, position);
        _units.Add(unit);
        _entities[unit.Id] = unit;
        return unit;
    }

    private BuildingEntity AddBuilding(int owner, BuildingType type, Point position, bool completed)
    {
        var building = new BuildingEntity(_nextEntityId++, owner, type, position, completed);
        _buildings.Add(building);
        _entities[building.Id] = building;
        _buildingAt[Index(position)] = building.Id;
        return building;
    }

    /// <summary>Nearest open tile around <paramref name="center"/>, searched ring by ring in a fixed order.</summary>
    private Point? FindFreeTileNear(Point center, int maxRadius = 8)
    {
        for (var r = 1; r <= maxRadius; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }

                    var p = new Point(center.X + dx, center.Y + dy);
                    if (Passable(p) && !_units.Any(u => u.Position == p))
                    {
                        return p;
                    }
                }
            }
        }

        // Every nearby tile has a unit on it; stacking is allowed, so settle for any open tile.
        for (var r = 1; r <= maxRadius; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    var p = new Point(center.X + dx, center.Y + dy);
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && Passable(p))
                    {
                        return p;
                    }
                }
            }
        }

        return null;
    }

    private int StorageCapacity(int slot) =>
        _buildings.Where(b => b.Owner == slot && b.Completed).Sum(b => b.Stats.StorageBonus);

    private ScoreDto ScoreOf(PlayerState p) => new()
    {
        Destruction = p.Destruction,
        Economy = p.Gathered,
        Survival = _units.Where(u => u.Owner == p.Slot).Sum(u => u.Stats.Cost)
                   + _buildings.Where(b => b.Owner == p.Slot && b.Completed).Sum(b => b.Stats.Cost),
    };

    private void AddEvent(PlayerState player, string kind, string message, int? entityId = null, long? commandId = null, bool spectator = true)
    {
        var evt = new GameEventDto { Tick = Tick, Kind = kind, Message = message, EntityId = entityId, CommandId = commandId };
        player.Events.Add(evt);
        if (player.Events.Count > Config.EventHistory)
        {
            player.Events.RemoveRange(0, player.Events.Count - Config.EventHistory);
        }

        if (spectator)
        {
            _spectatorEvents.Add(evt with { Message = $"{player.Name}: {message}", Owner = player.Slot });
            if (_spectatorEvents.Count > Config.EventHistory)
            {
                _spectatorEvents.RemoveRange(0, _spectatorEvents.Count - Config.EventHistory);
            }
        }
    }

    private bool[] VisibilityFor(int slot)
    {
        if (_visibility is null)
        {
            _visibility = new bool[_players.Count][];
            for (var s = 0; s < _players.Count; s++)
            {
                _visibility[s] = ComputeVisibility(s);
            }
        }

        return _visibility[slot];
    }

    private bool[] ComputeVisibility(int slot)
    {
        var grid = new bool[Map.Width * Map.Height];

        void Reveal(Point c, int radius)
        {
            var r2 = radius * radius;
            for (var dy = -radius; dy <= radius; dy++)
            {
                var y = c.Y + dy;
                if (y < 0 || y >= Map.Height)
                {
                    continue;
                }

                for (var dx = -radius; dx <= radius; dx++)
                {
                    var x = c.X + dx;
                    if (x >= 0 && x < Map.Width && dx * dx + dy * dy <= r2)
                    {
                        grid[y * Map.Width + x] = true;
                    }
                }
            }
        }

        foreach (var u in _units.Where(u => u.Owner == slot))
        {
            Reveal(u.Position, u.Stats.Vision);
        }

        foreach (var b in _buildings.Where(b => b.Owner == slot))
        {
            Reveal(b.Position, b.Completed ? b.Stats.Vision : GameRules.UnderConstructionVision);
        }

        return grid;
    }

    private bool IsVisible(bool[] visibility, Point p) => visibility[Index(p)];
}
