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

    internal void GrantUpgrade(int slot, UpgradeType upgrade) => _players[slot].Upgrades.Add(upgrade);

    internal void SetHp(int entityId, int hp) => _entities[entityId].Hp = hp;

    internal void RemoveEntity(int entityId)
    {
        var entity = _entities[entityId];
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

    internal int CommandCenterId(int slot) =>
        _buildings.First(b => b.Owner == slot && b.Type == BuildingType.CommandCenter).Id;

    internal void Run(int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            Step();
        }
    }
}
