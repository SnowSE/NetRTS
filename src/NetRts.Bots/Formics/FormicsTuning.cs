namespace NetRts.Bots.Formics;

/// <summary>The hive's numbers, in one place so they can be tuned against other bots.</summary>
internal sealed record FormicsTuning
{
    public static FormicsTuning Default { get; } = new();

    // ---------------------------------------------------------------- economy

    /// <summary>The most workers the hive keeps.</summary>
    public int MaxWorkers { get; init; } = 115;

    /// <summary>Workers wanted per this many grubs left in the fields we know of (plus a floor).</summary>
    public int OrePerWorker { get; init; } = 150;

    public int MinWorkers { get; init; } = 14;

    /// <summary>A worker goes looking for grub fields once there are this many.</summary>
    public int ScoutAtWorkers { get; init; } = 8;

    /// <summary>Command Centers wanted at these worker counts (the first is the one we start with).</summary>
    public IReadOnlyList<int> CommandCenterAtWorkers { get; init; } = [0, 8, 16, 30, 50, 70];

    /// <summary>A new Command Center's field must hold at least this many grubs.</summary>
    public int ExpansionMinOre { get; init; } = 2500;

    /// <summary>Keep new Command Centers at least this far from enemy buildings and start positions.</summary>
    public int ExpansionEnemyDistance { get; init; } = 14;

    /// <summary>Half-built sites nobody is working on get a builder (when no enemy is near).</summary>
    public bool FinishSites { get; init; } = true;

    /// <summary>A build site whose builder never got there is skipped for this long.</summary>
    public int FailedSiteTicks { get; init; } = 300;

    // ---------------------------------------------------------------- army

    /// <summary>The first Barracks waits for this many workers.</summary>
    public int FirstBarracksWorkers { get; init; } = 24;

    /// <summary>An enemy start this near ours makes a crowded map, a neighbor next door: Barracks and Tech Labs go behind the Command Center.</summary>
    public int CloseNeighborTiles { get; init; } = 30;

    /// <summary>One Barracks per this many grubs a tick of the income share for fighters (one turning out soldiers nonstop spends 8.3).</summary>
    public double IncomePerBarracks { get; init; } = 6.0;

    /// <summary>The share of income for fighters before <see cref="MidGameTick"/>, until <see cref="LateGameTick"/>, and after (all of it).</summary>
    public double EarlyArmyShare { get; init; } = 0.3;

    public double MidArmyShare { get; init; } = 0.6;

    public int MidGameTick { get; init; } = 250;

    public int LateGameTick { get; init; } = 500;

    /// <summary>A building saved up for longer than this goes ahead of workers and research.</summary>
    public int SaveTicks { get; init; } = 20;

    /// <summary>Research starts once there are this many workers.</summary>
    public int ResearchFromWorkers { get; init; } = 16;

    public int MaxBarracks { get; init; } = 18;

    /// <summary>One archer in this many fighters (0 = soldiers only).</summary>
    public int ArcherEvery { get; init; }

    /// <summary>Guard towers by each Command Center, once the base is this many workers big.</summary>
    public int TowersPerBase { get; init; } = 2;

    public int TowersFromWorkers { get; init; } = 30;

    /// <summary>Attack once the army is worth this many grubs...</summary>
    public int AttackValue { get; init; } = 9000;

    /// <summary>What a known enemy guard tower by the target counts for, in grubs of army.</summary>
    public int TowerValue { get; init; } = 300;

    /// <summary>...and this many times what we know the enemy has.</summary>
    public double AttackEdge { get; init; } = 1.0;

    /// <summary>Choosing a target, each known enemy tower by it counts as this many tiles further away.</summary>
    public int TowerDetour { get; init; } = 6;

    /// <summary>Income under this share of its peak (after tick 300) means our grubs are running out...</summary>
    public double StarvingIncomeShare { get; init; } = 0.35;

    /// <summary>...and then attack with this much of an edge (and at least 1,500 grubs of army).</summary>
    public double StarvingAttackEdge { get; init; } = 0.9;

    /// <summary>...and in a crowd, this much.</summary>
    public double CrowdStarvingAttackEdge { get; init; } = 1.5;

    /// <summary>Call the attack off when the army falls under this share of what it set out with.</summary>
    public double RetreatShare { get; init; } = 0.45;

    /// <summary>From this tick, attack with everything regardless.</summary>
    public int AllInTick { get; init; } = 1100;

    /// <summary>Grubs a tick an enemy worker is taken to dig, to guess their workers from their income.</summary>
    public double EnemyOrePerWorkerTick { get; init; } = 0.9;

    /// <summary>How much of what the scoreboard suggests to believe about an enemy army.</summary>
    public double ScoreArmyShare { get; init; } = 0.8;

    /// <summary>Once there's a Barracks, keep this many grubs of army per worker.</summary>
    public int ArmyPerWorker { get; init; } = 25;

    /// <summary>An expected enemy army this much bigger than ours and our towers puts Barracks and towers before expansions.</summary>
    public int OutgunnedMargin { get; init; } = 700;

    /// <summary>In a crowd, the strongest enemy army counts for this share of what it would one on one (0: only duels).</summary>
    public double CrowdThreatShare { get; init; } = 1.0;

    /// <summary>In a crowd, an enemy army counts for less the farther its base is from ours.</summary>
    public bool CrowdByDistance { get; init; } = true;

    /// <summary>Research before fighters, even when outnumbered (not when overrun).</summary>
    public bool ResearchFirst { get; init; } = true;

    /// <summary>A Tech Lab as soon as the first Barracks stands, before anything else.</summary>
    public bool LabFirst { get; init; } = true;

    /// <summary>A second Tech Lab once there are this many workers.</summary>
    public int SecondLabWorkers { get; init; } = 30;

    /// <summary>Weapons and Armor right after Entomology, before more Entomology.</summary>
    public bool EarlyFightUpgrades { get; init; } = true;

    /// <summary>Take the serum in the first fight with this many of our soldiers in it (0: never).</summary>
    public int SerumSoldiers { get; init; } = 8;

    /// <summary>...facing enemy fighters worth at least this much, and this share of our army.</summary>
    public int SerumFacing { get; init; } = 1500;

    public double SerumFacingShare { get; init; } = 0.6;

    /// <summary>Workers fleeing raiders go to a pile by our drop-offs away from them (off: the best pile not in danger).</summary>
    public bool FleeHome { get; init; } = true;

    /// <summary>Judge the enemy army by what it will be this many ticks on, at the rate it's growing.</summary>
    public int LookaheadTicks { get; init; } = 50;

    /// <summary>Keep the army at least this share of the strongest enemy army we expect.</summary>
    public double DefendShare { get; init; } = 0.9;

    /// <summary>Enemy fighters not seen for this long are forgotten.</summary>
    public int ForgetEnemyTicks { get; init; } = 250;

    /// <summary>What a worker counts for in a fight, in grubs of army.</summary>
    public int WorkerFightValue { get; init; } = 35;

    /// <summary>Each fighter picks its own target (off: attack-move only, for comparison).</summary>
    public bool FocusFire { get; init; } = true;

    /// <summary>Enemies this close to one of our buildings are a threat.</summary>
    public int DefenseRadius { get; init; } = 9;

    /// <summary>Where the rules have farms: build them once the piles near our drop-offs hold less than this (scaled by the map's grubs).</summary>
    public int FarmsBelowOre { get; init; } = 1500;

    /// <summary>At most this many farms (and one per five workers).</summary>
    public int MaxFarms { get; init; } = 4;
}
