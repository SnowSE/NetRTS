namespace NetRts.Protocol;

// The pick-up game (#/pickup): walk-up king of the hill. A phone builds a plan out of cards, the server
// plays it on the big screen against whoever holds the hill. Nothing here is ever saved.

/// <summary>Something bought from the 1,500-grub start pile: a unit or building type (API name) and how many.</summary>
public sealed record PickupBuyDto(string Item, int Count);

/// <summary>
/// One step of a plan, done once, in order. Kind: workers ("up to Count"), train (Item "up to Count"),
/// build (Item), research (Item, an upgrade). Keep (workers, train): after the step, replace any that die,
/// before working on later steps.
/// </summary>
public sealed record PickupStepDto
{
    public string Kind { get; init; } = "train";
    public string? Item { get; init; }
    public int Count { get; init; }
    public bool Keep { get; init; }
}

/// <summary>
/// When to attack: Trigger "army" (when you have Army fighters) or "time" (at AtSecond seconds); Target
/// "base" (the nearest enemy HQ) or "nearest" (the nearest enemies); Defend: turn back when your base is attacked.
/// </summary>
public sealed record PickupAttackDto
{
    public string Trigger { get; init; } = "army";
    public int Army { get; init; } = 12;
    public int AtSecond { get; init; } = 90;
    public string Target { get; init; } = "base";
    public bool Defend { get; init; } = true;

    /// <summary>
    /// Who to go for first, in order: towers (Bell Towers), workers, base (their HQ, else their nearest building),
    /// nearest (the nearest enemies). The first one the army can see wins. Null: just Target.
    /// </summary>
    public IReadOnlyList<string>? Priority { get; init; }
}

/// <summary>A pick-up plan: what to buy before the match, the steps, the Forever cards (repeated in order) and the attack.</summary>
public sealed record PickupPlanDto
{
    public IReadOnlyList<PickupBuyDto> Start { get; init; } = [];
    public IReadOnlyList<PickupStepDto> Steps { get; init; } = [];
    public IReadOnlyList<string> Forever { get; init; } = ["Soldier"];
    public PickupAttackDto Attack { get; init; } = new();
}

/// <summary>
/// Why the step being worked on hasn't happened yet. Reason: building (needs Item built first), class (needs
/// the upgrade Item first), grubs (saving up: Have of Cost), busy (every Item is busy), going-up (an Item is
/// being built), site (no room to build), builder (no free worker).
/// </summary>
public sealed record PickupWaitDto(string Reason, string? Item = null, int Have = 0, int Cost = 0);

/// <summary>A ready-made plan: what the house plays, and what a phone loads with one tap.</summary>
public sealed record PickupDeckDto(string Id, string Name, string HouseName, PickupPlanDto Plan);

/// <summary>A player as the phone and the big screen show them.</summary>
public sealed record PickupPlayerDto
{
    public string Name { get; init; } = "";

    /// <summary>The player's slot in the match (their color on the map), once the match has started.</summary>
    public int? Slot { get; init; }
    public bool You { get; init; }
    public bool Champion { get; init; }
    public bool House { get; init; }
    public int Grubs { get; init; }
    public int Workers { get; init; }
    public int Fighters { get; init; }
    public int Buildings { get; init; }
    public int Score { get; init; }
}

/// <summary>How your last match went (the match-over screen).</summary>
public sealed record PickupResultDto
{
    public int Place { get; init; }
    public bool Won { get; init; }
    public IReadOnlyList<PickupPlayerDto> Players { get; init; } = [];
    public int StepsDone { get; init; }
    public int Steps { get; init; }
    public int FightersTrained { get; init; }
    public int FightersLost { get; init; }
    public int Buildings { get; init; }
    public int Classes { get; init; }
}

/// <summary>
/// One phone's view. Phase: line (waiting), next (first in line, planning time left in SecondsLeft),
/// countdown (your match starts in SecondsLeft), playing, over (Result says how it went).
/// </summary>
public sealed record PickupMeDto
{
    public string Token { get; init; } = "";
    public string Name { get; init; } = "";
    public string Phase { get; init; } = "line";
    public int? Place { get; init; }
    public int? SecondsLeft { get; init; }
    public Guid? MatchId { get; init; }
    public bool Champion { get; init; }
    public int Wins { get; init; }

    /// <summary>Not ready yet: keeps your place in line while the people behind you go ahead.</summary>
    public bool Holding { get; init; }
    public int Step { get; init; }
    public int Steps { get; init; }

    /// <summary>While playing: why the current step is waiting (null when it's going ahead).</summary>
    public PickupWaitDto? Waiting { get; init; }
    public IReadOnlyList<PickupPlayerDto> Players { get; init; } = [];
    public PickupResultDto? Result { get; init; }
    public PickupPlanDto Plan { get; init; } = new();
}

/// <summary>The big screen: the match on now, who holds the hill, who's playing and who's in line.</summary>
public sealed record PickupHillDto
{
    public Guid? MatchId { get; init; }
    public string Phase { get; init; } = "idle";
    public PickupPlayerDto? Champion { get; init; }
    public int ChampionWins { get; init; }
    public IReadOnlyList<PickupPlayerDto> Playing { get; init; } = [];
    public IReadOnlyList<string> Line { get; init; } = [];
    public int? SecondsLeft { get; init; }

    /// <summary>The match on now is the house keeping the screen busy: it stops as soon as a player is ready.</summary>
    public bool Warmup { get; init; }
}

/// <summary>Name: the name this phone had, so it keeps it if the server restarted (only one of the game's own names is accepted).</summary>
public sealed record PickupTokenRequest(string? Token, string? Name = null);

public sealed record PickupPlanRequest(string Token, PickupPlanDto Plan);

public sealed record PickupOrderRequest(string Token, string Order);

public sealed record PickupHoldRequest(string Token, bool Hold);
