using NetRts.Protocol;

namespace NetRts.Bots.Blocks;

/// <summary>
/// Which of a bot's own units and buildings were hurt lately, for Block Bot's "under attack" check: something
/// that lost health since the last tick, or is gone (destroyed), counts for <see cref="Window"/> ticks.
/// </summary>
public sealed class AttackWatch
{
    /// <summary>How many ticks (seconds) a hit keeps something "under attack".</summary>
    public const int Window = 5;

    private readonly Dictionary<(bool IsUnit, int Id), (string Type, int Hp)> _last = [];
    private readonly List<(bool IsUnit, string Type, int Tick)> _hits = [];

    /// <summary>Compares this tick with the last one. Call once a tick, before the rules run.</summary>
    public void Update(BotView view)
    {
        var tick = view.State.Tick;
        var now = new Dictionary<(bool, int), (string, int)>();
        foreach (var u in view.MyUnits)
        {
            now[(true, u.Id)] = (u.Type.ToString(), u.Hp);
        }

        foreach (var b in view.MyBuildings)
        {
            now[(false, b.Id)] = (b.Type.ToString(), b.Hp);
        }

        foreach (var (key, (type, hp)) in _last)
        {
            if (!now.TryGetValue(key, out var after) || after.Item2 < hp)
            {
                _hits.Add((key.IsUnit, type, tick));
            }
        }

        _last.Clear();
        foreach (var (key, value) in now)
        {
            _last[key] = value;
        }

        _hits.RemoveAll(h => tick - h.Tick > Window);
    }

    /// <summary>
    /// Whether any of what (units, workers, fighters, buildings, a unit type or a building type) was hurt in the
    /// last <see cref="Window"/> ticks.
    /// </summary>
    public bool Any(string what, int tick) => _hits.Any(h => tick - h.Tick <= Window && Matches(what, h.IsUnit, h.Type));

    private static bool Matches(string what, bool isUnit, string type) => what.ToLowerInvariant() switch
    {
        "units" => isUnit,
        "workers" => isUnit && type == nameof(UnitType.Worker),
        "fighters" => isUnit && type != nameof(UnitType.Worker),
        "buildings" => !isUnit,
        _ => string.Equals(type, what, StringComparison.OrdinalIgnoreCase)
             && isUnit == Enum.TryParse<UnitType>(what, true, out _),
    };
}
