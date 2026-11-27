using NetRts.Protocol;

namespace NetRts.Engine;

// Hooks for engine tests to stage precise scenarios. Not reachable from the server.
public sealed partial class GameSimulation
{
    internal int SpawnUnit(int slot, UnitType type, int x, int y) => AddUnit(slot, type, new Point(x, y)).Id;

    internal int SpawnBuilding(int slot, BuildingType type, int x, int y, bool completed = true) =>
        AddBuilding(slot, type, new Point(x, y), completed).Id;

    internal int SpawnDeposit(int x, int y, int amount)
    {
        var deposit = new DepositEntity(_nextEntityId++, new Point(x, y), amount);
        _deposits.Add(deposit);
        _entities[deposit.Id] = deposit;
        _depositAt[Index(deposit.Position)] = deposit.Id;
        return deposit.Id;
    }

    internal void SetResources(int slot, int amount) => _players[slot].Resources = amount;

    internal int Resources(int slot) => _players[slot].Resources;

    internal void SetGathered(int slot, int amount) => _players[slot].Gathered = amount;

    internal void GrantUpgrade(int slot, UpgradeType upgrade) => _players[slot].Upgrades.Add(upgrade);

    internal void SetHp(int entityId, int hp) => _entities[entityId].Hp = hp;

    internal void RemoveEntity(int entityId)
    {
        var entity = _entities[entityId];
        if (entity is UnitEntity unit)
        {
            Unsettle(unit);
        }

        entity.Hp = 0;
        _units.RemoveAll(u => u.Id == entityId);
        _buildings.RemoveAll(b => b.Id == entityId);
        if (entity is BuildingEntity)
        {
            _buildingAt[Index(entity.Position)] = 0;
        }

        _entities.Remove(entityId);
        _visibility = null;
    }

    /// <summary>Removes every unit, for clean-room scenarios.</summary>
    internal void ClearArmies()
    {
        foreach (var u in _units.ToList())
        {
            RemoveEntity(u.Id);
        }
    }

    internal (int X, int Y, int Hp)? Find(int entityId) =>
        _entities.TryGetValue(entityId, out var e) ? (e.Position.X, e.Position.Y, e.Hp) : null;

    internal IEnumerable<int> UnitIds(int slot, UnitType? type = null) =>
        _units.Where(u => u.Owner == slot && (type is null || u.Type == type)).Select(u => u.Id);

    internal IEnumerable<(BuildingType Type, int X, int Y, bool Completed)> BuildingsOf(int slot) =>
        _buildings.Where(b => b.Owner == slot).Select(b => (b.Type, b.Position.X, b.Position.Y, b.Completed));

    internal int CommandCenterId(int slot) =>
        _buildings.First(b => b.Owner == slot && b.Type == BuildingType.CommandCenter).Id;

    internal void SetFarmGrubs(int farmId, int grubs) => ((BuildingEntity)_entities[farmId]).Grubs = grubs;

    internal int FarmGrubs(int farmId) => ((BuildingEntity)_entities[farmId]).Grubs;

    /// <summary>
    /// What's wrong with the cell bookkeeping, or null: every unit holds a place, every cell's load must be what
    /// its units add up to, within <see cref="RuleSet.CellCapacity"/>, and no cell may have more diggers than <see cref="RuleSet.MinersPerEdge"/>.
    /// </summary>
    internal string? CellProblem()
    {
        if (_units.FirstOrDefault(u => u.SettledSeq == 0) is { } loose)
        {
            return $"unit {loose.Id} at {loose.Position} holds no place";
        }

        var load = new int[_cellLoad.Length];
        foreach (var unit in _units.Where(u => u.SettledSeq != 0))
        {
            load[Index(unit.Position)] += unit.Stats.Occupancy;
            if (!_standing[Index(unit.Position)].Contains(unit))
            {
                return $"unit {unit.Id} holds a place at {unit.Position} but isn't listed there";
            }
        }

        for (var i = 0; i < load.Length; i++)
        {
            if (load[i] != _cellLoad[i])
            {
                return $"cell {i} has load {_cellLoad[i]} but its units add up to {load[i]}";
            }

            if (load[i] > Rules.CellCapacity)
            {
                return $"cell {i} holds {load[i]} points, over {Rules.CellCapacity}";
            }
        }

        var crowded = _units.Where(u => u.Activity == UnitActivity.Gathering && u.GatherSpot == u.Position)
            .GroupBy(u => u.Position).FirstOrDefault(g => g.Count() > Rules.MinersPerEdge);
        return crowded is null ? null : $"{crowded.Count()} workers dig from {crowded.Key}";
    }

    internal void Run(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            Step();
        }
    }
}
