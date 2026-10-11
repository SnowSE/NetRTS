using System.Text.Json.Serialization;

namespace NetRts.Protocol;

[JsonConverter(typeof(JsonStringEnumConverter<UnitType>))]
public enum UnitType
{
    Worker,
    Soldier,
    Archer,
    Scout,
}

[JsonConverter(typeof(JsonStringEnumConverter<BuildingType>))]
public enum BuildingType
{
    CommandCenter,
    Barracks,
    ResourceDepot,
    TechLab,
    GuardTower,
}

[JsonConverter(typeof(JsonStringEnumConverter<UpgradeType>))]
public enum UpgradeType
{
    Weapons1,
    Weapons2,
    Armor1,
    Armor2,
    Mobility1,
    Mobility2,
    Harvesting1,
    Harvesting2,
}

[JsonConverter(typeof(JsonStringEnumConverter<CommandType>))]
public enum CommandType
{
    /// <summary>Walk to a tile, ignoring enemies.</summary>
    Move,
    /// <summary>Attack an entity (targetId), or attack-move to a tile (x/y or tile).</summary>
    Attack,
    /// <summary>Workers mine a resource deposit (targetId) and haul ore home until it runs dry.</summary>
    Gather,
    /// <summary>Workers construct (or help construct) a building at a tile.</summary>
    Build,
    /// <summary>A building (buildingId) queues production of unitType.</summary>
    Produce,
    /// <summary>A TechLab (buildingId) researches an upgrade.</summary>
    Research,
    /// <summary>Units drop their current order and go idle.</summary>
    Stop,
    /// <summary>A building (buildingId) sends the units it trains to a tile (x/y or tile). Workers sent to ore start mining.</summary>
    Rally,
}

/// <summary>Which leaderboard a match counts toward. Each category keeps its own ratings and records.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MatchCategory>))]
public enum MatchCategory
{
    /// <summary>Any match a bot creates. Open to everyone.</summary>
    Practice,

    /// <summary>Warm-up league matches (e.g. beta testing) that keep the season's ladder clean. Verified bots only.</summary>
    Preseason,

    /// <summary>Regular-season league match, created by an admin and played in heats. Verified bots only.</summary>
    Season,

    /// <summary>Championship league match, created by an admin and played in heats. Verified bots only.</summary>
    Championship,
}

[JsonConverter(typeof(JsonStringEnumConverter<MatchStatus>))]
public enum MatchStatus
{
    Waiting,
    Active,
    Completed,
}

[JsonConverter(typeof(JsonStringEnumConverter<UnitActivity>))]
public enum UnitActivity
{
    Idle,
    Moving,
    Attacking,
    Gathering,
    Returning,
    Building,
}

[JsonConverter(typeof(JsonStringEnumConverter<MatchEndReason>))]
public enum MatchEndReason
{
    Elimination,
    TimeLimit,
    Surrender,
}
