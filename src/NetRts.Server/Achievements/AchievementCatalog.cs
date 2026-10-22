using NetRts.Protocol;
using NetRts.Server.Data;

namespace NetRts.Server.Achievements;

/// <summary>
/// One achievement. <see cref="Check"/> runs for each bot in each finished match it might be earned
/// in; achievements without one are awarded elsewhere (the nightly ladder snapshot).
/// </summary>
/// <param name="Id">Stable id, stored in the database.</param>
/// <param name="Name">What it's called.</param>
/// <param name="Description">What earns it.</param>
/// <param name="Group">Where it's listed.</param>
/// <param name="Tier">How hard it is.</param>
/// <param name="Check">Whether a bot's match earns it; null for achievements awarded elsewhere.</param>
/// <param name="HouseBotsCount">Whether a match with no real opponent (only house bots, or the owner's own bots) counts.</param>
/// <param name="Hidden">Name and description stay secret until someone earns it.</param>
public sealed record Achievement(
    string Id,
    string Name,
    string Description,
    string Group,
    AchievementTier Tier,
    Func<AchievementContext, bool>? Check,
    bool HouseBotsCount = false,
    bool Hidden = false);

/// <summary>
/// Every achievement. Ids are stored in the database: never change one. Names, descriptions and
/// thresholds may change (thresholds only apply to matches from then on). docs/bot-guide.md lists them.
/// </summary>
public static class AchievementCatalog
{
    public const string FirstSteps = "First steps";
    public const string Winning = "Winning";
    public const string Economy = "Economy";
    public const string Combat = "Combat";
    public const string Speed = "Speed and precision";
    public const string ShowingUp = "Showing up";
    public const string Ladder = "Ladder and league";
    public const string Fun = "Just for fun";

    public const string TopTen = "top-ten";
    public const string NumberOne = "number-one";

    private const AchievementTier Bronze = AchievementTier.Bronze;
    private const AchievementTier Silver = AchievementTier.Silver;
    private const AchievementTier Gold = AchievementTier.Gold;
    private const AchievementTier Platinum = AchievementTier.Platinum;

    public static readonly IReadOnlyList<Achievement> All =
    [
        // First steps: house bots count, so a new bot gets going against them.
        new("hello-world", "Hello, World", "Finish your first match.", FirstSteps, Bronze, _ => true, HouseBotsCount: true),
        new("first-blood", "First Blood", "Kill an enemy unit.", FirstSteps, Bronze, c => c.Result.UnitsKilled > 0, HouseBotsCount: true),
        new("breaking-ground", "Breaking Ground", "Complete a building other than your starting Command Center.", FirstSteps, Bronze,
            c => c.Stats?.BuildingsCompleted.Values.Sum() > 0, HouseBotsCount: true),
        new("boot-camp", "Boot Camp", "Train your first Soldier, Archer or Scout.", FirstSteps, Bronze,
            c => c.Stats?.FirstCombatUnitTick is not null, HouseBotsCount: true),
        new("lab-coat", "Lab Coat", "Complete your first research.", FirstSteps, Bronze, c => c.Stats?.Upgrades.Count > 0, HouseBotsCount: true),
        new("training-wheels-off", "Training Wheels Off", "Beat a house bot.", FirstSteps, Bronze,
            c => c.Won && c.Others.Any(o => o.IsHouseBot), HouseBotsCount: true),
        new("full-house", "Full House", "Beat every house bot (rusher, economist, balanced and sitter), over any number of matches.", FirstSteps, Silver,
            c => c.Won && c.HouseBotIds.Count > 0 && c.HouseBotIds.All(c.HasBeaten), HouseBotsCount: true),
        new("blue-check", "Blue Check", "Play a match after a Discord-verified owner claims your bot.", FirstSteps, Bronze, c => c.Verified, HouseBotsCount: true),
        new("worthy-opponent", "Worthy Opponent", "Play a match against another real bot.", FirstSteps, Bronze, _ => true),
        new("suit-up", "Suit Up", "Play in a league match (preseason, season or championship).", FirstSteps, Bronze,
            c => c.Row.Category != MatchCategory.Practice),

        // Winning
        new("first-win", "First Win", "Win a match against a real bot.", Winning, Bronze, c => c.Won),
        new("double-digits", "Double Digits", "Win 10 matches against real bots.", Winning, Silver, c => c.Won && c.RealWins >= 10),
        new("centurion", "Centurion", "Win 100 matches against real bots.", Winning, Gold, c => c.Won && c.RealWins >= 100),
        new("hat-trick", "Hat Trick", "Win 3 matches against real bots in a row.", Winning, Silver, c => c.WinStreak >= 3),
        new("unstoppable", "Unstoppable", "Win 10 matches against real bots in a row.", Winning, Gold, c => c.WinStreak >= 10),
        new("blitz", "Blitz", "Win by elimination before tick 300.", Winning, Silver,
            c => c.Won && c.Row.Reason == MatchEndReason.Elimination && c.Row.Ticks < 300),
        new("flawless-victory", "Flawless Victory", "Win without losing a single unit or building.", Winning, Gold,
            c => c.Won && c.Result.UnitsLost == 0 && c.Result.BuildingsLost == 0),
        new("photo-finish", "Photo Finish", "Win on the time limit by less than 2% of the runner-up's score.", Winning, Silver,
            c => c.Won && c.Row.Reason == MatchEndReason.TimeLimit && c.RunnerUpScore is int r && r > 0 && (c.Row.Score - r) * 50 < r),
        new("buzzer-beater", "Buzzer Beater", "Win by elimination in the last 60 ticks before the time limit.", Winning, Silver,
            c => c.Won && c.Row.Reason == MatchEndReason.Elimination && c.Row.Ticks > c.MaxTicks - 60),
        new("comeback-kid", "Comeback Kid", "Win after your score fell to half the leader's or less (from tick 100 on).", Winning, Gold,
            c => c.Won && c.Stats?.LowestScoreSharePercent <= 50),
        new("last-bot-standing", "Last Bot Standing", "Win a match with 8 or more players.", Winning, Gold, c => c.Won && c.Row.PlayerCount >= 8),
        new("battle-royale", "Battle Royale", "Win a 16-player match.", Winning, Platinum, c => c.Won && c.Row.PlayerCount >= 16),
        new("giant-killer", "Giant Killer", "Beat a bot rated 200 or more above you in a rated match.", Winning, Gold,
            c => c.Won && c.Row.RatingBefore is int mine && c.Others.Any(o => o.RatingBefore >= mine + 200)),

        // Economy
        new("ore-inspector", "Ore Inspector", "Gather 1,000 ore in one match.", Economy, Bronze, c => c.Row.Gathered >= 1_000, HouseBotsCount: true),
        new("ore-baron", "Ore Baron", "Gather 10,000 ore in one match.", Economy, Silver, c => c.Row.Gathered >= 10_000),
        new("ore-tycoon", "Ore Tycoon", "Gather 20,000 ore in one match.", Economy, Gold, c => c.Row.Gathered >= 20_000),
        new("strip-miner", "Strip Miner", "Mine 3 resource deposits dry in one match.", Economy, Silver, c => c.Stats?.DepositsExhausted >= 3),
        new("expansionist", "Expansionist", "Complete 3 Resource Depots in one match.", Economy, Bronze, c => c.Completed(BuildingType.ResourceDepot) >= 3),
        new("second-home", "Second Home", "Complete a second Command Center.", Economy, Silver, c => c.Completed(BuildingType.CommandCenter) >= 1),
        new("union-strong", "Union Strong", "Have 30 workers alive at once.", Economy, Silver, c => c.Stats?.PeakWorkers >= 30),
        new("dragons-hoard", "Dragon's Hoard", "Bank 3,000 unspent ore at one moment.", Economy, Bronze, c => c.Stats?.PeakResources >= 3_000),
        new("waste-not", "Waste Not", "Finish a match of 600+ ticks without losing any ore to the storage cap.", Economy, Gold,
            c => c.Row.Ticks >= 600 && c.Row.Gathered > 0 && c.Stats?.OreWasted == 0),
        new("efficiency-expert", "Efficiency Expert", "Research Harvesting 2.", Economy, Bronze, c => c.Researched(UpgradeType.Harvesting2)),
        new("lifetime-haul", "Lifetime Haul", "Gather 1,000,000 ore across your matches against real bots.", Economy, Platinum, c => c.RealGathered >= 1_000_000),

        // Combat
        new("massacre", "Massacre", "Kill 50 enemy units in one match.", Combat, Silver, c => c.Row.UnitsKilled >= 50),
        new("wrecking-crew", "Wrecking Crew", "Destroy 10 enemy buildings in one match.", Combat, Silver, c => c.Row.BuildingsDestroyed >= 10),
        new("regicide", "Regicide", "Destroy an enemy Command Center.", Combat, Bronze, c => c.Stats?.CommandCentersDestroyed > 0),
        new("body-count", "Body Count", "Kill 1,000 enemy units across your matches against real bots.", Combat, Gold, c => c.RealKills >= 1_000),
        new("hold-the-line", "Hold the Line", "Your guard towers kill 20 enemy units in one match.", Combat, Silver, c => c.Stats?.TowerKills >= 20),
        new("tower-defense", "Tower Defense", "Complete 6 guard towers in one match.", Combat, Bronze, c => c.Completed(BuildingType.GuardTower) >= 6),
        new("robin-hood", "Robin Hood", "Win having trained only Archers (no Soldiers or Scouts).", Combat, Gold,
            c => c.Won && c.OnlyTrained(UnitType.Archer)),
        new("shield-wall", "Shield Wall", "Win having trained only Soldiers (no Archers or Scouts).", Combat, Silver,
            c => c.Won && c.OnlyTrained(UnitType.Soldier)),
        new("cavalry-charge", "Cavalry Charge", "Win having trained only Scouts as combat units.", Combat, Gold,
            c => c.Won && c.OnlyTrained(UnitType.Scout)),
        new("worker-uprising", "Worker Uprising", "Win without ever building a Barracks.", Combat, Platinum,
            c => c.Won && c.Stats is not null && c.Completed(BuildingType.Barracks) == 0),
        new("arms-race", "Arms Race", "Research Weapons 2 and Armor 2 in the same match.", Combat, Silver,
            c => c.Researched(UpgradeType.Weapons2) && c.Researched(UpgradeType.Armor2)),
        new("fully-loaded", "Fully Loaded", "Research all 8 upgrades in one match.", Combat, Gold,
            c => c.Stats?.Upgrades.Count >= Enum.GetValues<UpgradeType>().Length),
        new("super-soldier", "Super Soldier", "Take the serum.", Combat, Gold, c => c.Stats?.SerumTaken == true, Hidden: true),
        new("not-a-perfect-soldier", "Not a Perfect Soldier", "Kill 10 enemy units in one match with serum-boosted Soldiers.", Combat, Platinum,
            c => c.Stats?.SerumKills >= 10, Hidden: true),

        // Speed and precision
        new("quick-draw", "Quick Draw", "Complete a Barracks before tick 60.", Speed, Silver, c => c.Stats?.FirstBarracksTick < 60),
        new("first-to-the-fight", "First to the Fight", "Have your first combat unit ready before tick 75.", Speed, Silver, c => c.Stats?.FirstCombatUnitTick < 75),
        new("eagle-eye", "Eagle Eye", "See an enemy Command Center before tick 100.", Speed, Silver, c => c.Stats?.EnemyCommandCenterSeenTick < 100),
        new("clean-code", "Clean Code", "Finish a match of 300+ ticks with no rejected or failed commands.", Speed, Silver,
            c => c.Row.Ticks >= 300 && c.Stats is { CommandsQueued: > 0, CommandsRejected: 0, CommandsFailed: 0 }),
        new("less-is-more", "Less Is More", "Win having sent fewer than 50 commands.", Speed, Gold,
            c => c.Won && c.Stats is { } s && s.CommandsQueued + s.CommandsRejected < 50),
        new("busy-bees", "Busy Bees", "Finish a match of 600+ ticks with no worker idle for more than 10 ticks in a row.", Speed, Gold,
            c => c.Row.Ticks >= 600 && c.Stats?.LongestWorkerIdleTicks <= 10),
        new("set-it-and-forget-it", "Set It and Forget It", "Set rally points on 3 different buildings in one match.", Speed, Bronze,
            c => c.Stats?.BuildingsRallied >= 3),

        // Showing up (all counted over matches against real bots)
        new("regular", "Regular", "Play real bots on 7 different days.", ShowingUp, Bronze, c => c.RealDays >= 7),
        new("marathon", "Marathon", "Play 100 matches against real bots.", ShowingUp, Silver, c => c.Real.Count >= 100),
        new("iron-bot", "Iron Bot", "Play 1,000 matches against real bots.", ShowingUp, Gold, c => c.Real.Count >= 1_000),
        new("night-owl", "Night Owl", "Finish a match between midnight and 4 a.m.", ShowingUp, Bronze, c => c.LocalTime(c.Row.CompletedAt).Hour < 4),
        new("welcome-back", "Welcome Back", "Win within a day of coming back from 30 or more days away.", ShowingUp, Silver, c => c.Won && c.JustCameBack),
        new("social-butterfly", "Social Butterfly", "Play 10 different real bots.", ShowingUp, Silver, c => c.DistinctRealOpponents >= 10),
        new("rivalry", "Rivalry", "Play the same real bot 10 times.", ShowingUp, Bronze, c => c.CurrentRealOpponents.Any(o => c.TimesPlayed(o) >= 10)),
        new("nemesis-defeated", "Nemesis Defeated", "Beat a bot that has beaten you 5 or more times.", ShowingUp, Gold,
            c => c.Won && c.CurrentRealOpponents.Any(o => c.TimesBeatenBy(o) >= 5)),
        new("glow-up", "Glow Up", "Win 5 more of your last 20 matches against real bots than of your first 20.", ShowingUp, Gold, c => c.GlowUp >= 5),
        new("stable-owner", "Stable Owner", "Your owner has 3 verified bots that have each beaten a real bot.", ShowingUp, Gold,
            c => c.Verified && c.RealWins > 0 && c.OwnerBotsWithRealWins >= 3),

        // Ladder and league
        new("climber", "Climber", "Reach a rating of 1300 in any category (everyone starts at 1200).", Ladder, Silver, c => c.BestRating >= 1300),
        new("elite", "Elite", "Reach a rating of 1500 in any category.", Ladder, Gold, c => c.BestRating >= 1500),
        new(TopTen, "Top Ten", "Be in the top 10 of the season ladder when it's checked each night.", Ladder, Gold, null),
        new(NumberOne, "Number One", "Top the season ladder when it's checked each night.", Ladder, Platinum, null),
        new("heat-winner", "Heat Winner", "Win a season heat.", Ladder, Gold, c => c.Won && c.Row.Category == MatchCategory.Season),
        new("champion", "Champion", "Win a championship heat.", Ladder, Platinum, c => c.Won && c.Row.Category == MatchCategory.Championship),

        // Just for fun
        new("pacifist", "Pacifist", "Win on score without killing a single enemy unit.", Fun, Gold,
            c => c.Won && c.Row.Reason == MatchEndReason.TimeLimit && c.Row.UnitsKilled == 0),
        new("turtle-power", "Turtle Power", "Survive to the time limit with 8 or more guard towers standing.", Fun, Silver,
            c => c.Row.Reason == MatchEndReason.TimeLimit && c.Stats?.GuardTowersStanding >= 8),
        new("know-when-to-fold-em", "Know When to Fold 'Em", "Surrender a match.", Fun, Bronze, c => c.Row.Surrendered, HouseBotsCount: true),
        new("by-a-thread", "By a Thread", "Win with every Command Center you have left below 10% health.", Fun, Gold,
            c => c.Won && c.Stats?.CommandCenterHealthPercent < 10, Hidden: true),
    ];

    public static readonly IReadOnlyDictionary<string, Achievement> ById = All.ToDictionary(a => a.Id);
}
