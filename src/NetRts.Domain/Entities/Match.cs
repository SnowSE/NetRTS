using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.Domain.Entities;

/// <summary>
/// Represents a single RTS game instance between two players.
/// Aggregate root for match-related operations.
/// </summary>
public class Match
{
    /// <summary>
    /// Unique match identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Current match status.
    /// </summary>
    public MatchStatus Status { get; private set; }

    /// <summary>
    /// Current game tick number (0 to MaxTicksPerMatch).
    /// </summary>
    public int CurrentTick { get; private set; }

    /// <summary>
    /// Map width in tiles.
    /// </summary>
    public int MapWidth { get; private set; }

    /// <summary>
    /// Map height in tiles.
    /// </summary>
    public int MapHeight { get; private set; }

    /// <summary>
    /// Match duration limit in ticks.
    /// </summary>
    public int MaxTicksPerMatch { get; private set; }

    /// <summary>
    /// Milliseconds between ticks.
    /// </summary>
    public int TickIntervalMs { get; private set; }

    /// <summary>
    /// Max commands per player queue.
    /// </summary>
    public int CommandQueueSizeLimit { get; private set; }

    /// <summary>
    /// Commands processed per tick.
    /// </summary>
    public int CommandsPerTick { get; private set; }

    /// <summary>
    /// When match started (null if pending).
    /// </summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>
    /// When match ended (null if active).
    /// </summary>
    public DateTime? EndedAt { get; private set; }

    /// <summary>
    /// ID of winning player (null if active).
    /// </summary>
    public Guid? WinnerId { get; private set; }

    /// <summary>
    /// First player ID.
    /// </summary>
    public Guid Player1Id { get; private set; }

    /// <summary>
    /// Second player ID.
    /// </summary>
    public Guid Player2Id { get; private set; }

    /// <summary>
    /// Player 1 score breakdown.
    /// </summary>
    public Score Player1Score { get; private set; } = Score.Empty;

    /// <summary>
    /// Player 2 score breakdown.
    /// </summary>
    public Score Player2Score { get; private set; } = Score.Empty;

    /// <summary>
    /// Player 1 current resources.
    /// </summary>
    public int Player1Resources { get; private set; }

    /// <summary>
    /// Player 2 current resources.
    /// </summary>
    public int Player2Resources { get; private set; }

    /// <summary>
    /// Serialized game state snapshot for crash recovery.
    /// </summary>
    public byte[]? GameStateSnapshot { get; private set; }

    // Navigation properties
    private readonly List<Unit> _units = new();
    private readonly List<Building> _buildings = new();
    private readonly List<Command> _commands = new();
    private readonly List<MapTile> _mapTiles = new();
    private readonly List<ResourceDeposit> _resourceDeposits = new();
    private readonly List<Upgrade> _upgrades = new();

    public IReadOnlyList<Unit> Units => _units.AsReadOnly();
    public IReadOnlyList<Building> Buildings => _buildings.AsReadOnly();
    public IReadOnlyList<Command> Commands => _commands.AsReadOnly();
    public IReadOnlyList<MapTile> MapTiles => _mapTiles.AsReadOnly();
    public IReadOnlyList<ResourceDeposit> ResourceDeposits => _resourceDeposits.AsReadOnly();
    public IReadOnlyList<Upgrade> Upgrades => _upgrades.AsReadOnly();

    /// <summary>
    /// Get players with their upgrades for validation purposes.
    /// </summary>
    public IEnumerable<MatchPlayerState> Players
    {
        get
        {
            yield return new MatchPlayerState(Player1Id, _upgrades.Where(u => u.OwnerId == Player1Id).ToList());
            yield return new MatchPlayerState(Player2Id, _upgrades.Where(u => u.OwnerId == Player2Id).ToList());
        }
    }

    // EF Core constructor
    private Match() { }

    public Match(Guid player1Id, Guid player2Id, GameSettings settings)
    {
        Id = Guid.NewGuid();
        Status = MatchStatus.Pending;
        CurrentTick = 0;
        MapWidth = settings.MapWidth;
        MapHeight = settings.MapHeight;
        MaxTicksPerMatch = settings.MaxTicks;
        TickIntervalMs = settings.TickIntervalMs;
        CommandQueueSizeLimit = settings.CommandQueueSize;
        CommandsPerTick = settings.CommandsPerTick;
        Player1Id = player1Id;
        Player2Id = player2Id;
        Player1Resources = settings.StartingResources;
        Player2Resources = settings.StartingResources;
    }

    /// <summary>
    /// Start the match.
    /// </summary>
    public void Start()
    {
        if (Status != MatchStatus.Pending)
        {
            throw new InvalidOperationException("Match must be in Pending status to start.");
        }

        Status = MatchStatus.Active;
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Increment tick counter.
    /// </summary>
    public void AdvanceTick()
    {
        if (Status != MatchStatus.Active)
        {
            throw new InvalidOperationException("Cannot advance tick on non-active match.");
        }

        CurrentTick++;

        // Note: Time limit check is now handled by GameTickProcessor
        // which has access to units and buildings for final scoring
    }

    /// <summary>
    /// End match due to time limit. Caller must provide final unit and building counts.
    /// </summary>
    public void EndMatchByTimeLimit(int player1Units, int player1Buildings, int player2Units, int player2Buildings)
    {
        // Update final scores with remaining units and buildings
        Player1Score = Player1Score.WithUnitsRemaining(player1Units).WithBuildingsRemaining(player1Buildings);
        Player2Score = Player2Score.WithUnitsRemaining(player2Units).WithBuildingsRemaining(player2Buildings);

        var player1Total = Player1Score.TotalScore;
        var player2Total = Player2Score.TotalScore;

        WinnerId = player1Total >= player2Total ? Player1Id : Player2Id;
        Status = MatchStatus.Completed;
        EndedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// End match due to elimination (Command Center destroyed).
    /// </summary>
    public void EndMatchByElimination(Guid eliminatedPlayerId)
    {
        if (Status != MatchStatus.Active)
        {
            throw new InvalidOperationException("Cannot end non-active match.");
        }

        WinnerId = eliminatedPlayerId == Player1Id ? Player2Id : Player1Id;
        Status = MatchStatus.Completed;
        EndedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark match as abandoned.
    /// </summary>
    public void Abandon()
    {
        Status = MatchStatus.Abandoned;
        EndedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Update player resources.
    /// </summary>
    public void UpdateResources(Guid playerId, int amount)
    {
        if (playerId == Player1Id)
        {
            Player1Resources = Math.Max(0, Player1Resources + amount);
            // Only update score for positive amounts (resources gathered, not spent)
            if (amount > 0)
            {
                Player1Score = Player1Score.WithResourcesGathered(amount);
            }
        }
        else if (playerId == Player2Id)
        {
            Player2Resources = Math.Max(0, Player2Resources + amount);
            // Only update score for positive amounts (resources gathered, not spent)
            if (amount > 0)
            {
                Player2Score = Player2Score.WithResourcesGathered(amount);
            }
        }
    }

    /// <summary>
    /// Get resources for a specific player.
    /// </summary>
    public int GetPlayerResources(Guid playerId)
    {
        if (playerId == Player1Id) return Player1Resources;
        if (playerId == Player2Id) return Player2Resources;
        throw new ArgumentException($"Player {playerId} is not in this match.");
    }

    /// <summary>
    /// Set player resources to a specific amount.
    /// </summary>
    public void SetPlayerResources(Guid playerId, int amount)
    {
        if (playerId == Player1Id)
        {
            Player1Resources = Math.Max(0, amount);
        }
        else if (playerId == Player2Id)
        {
            Player2Resources = Math.Max(0, amount);
        }
    }

    /// <summary>
    /// Get score for a specific player.
    /// </summary>
    public Score GetPlayerScore(Guid playerId)
    {
        if (playerId == Player1Id) return Player1Score;
        if (playerId == Player2Id) return Player2Score;
        throw new ArgumentException($"Player {playerId} is not in this match.");
    }

    /// <summary>
    /// Deduct resources from a player. Returns true if successful, false if insufficient resources.
    /// </summary>
    public bool DeductResources(Guid playerId, int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Amount must be non-negative");
        }

        if (playerId == Player1Id)
        {
            if (Player1Resources < amount) return false;
            Player1Resources -= amount;
            return true;
        }
        else if (playerId == Player2Id)
        {
            if (Player2Resources < amount) return false;
            Player2Resources -= amount;
            return true;
        }

        throw new ArgumentException($"Player {playerId} is not in this match.");
    }

    /// <summary>
    /// Update player score.
    /// </summary>
    public void UpdateScore(Guid playerId, Score score)
    {
        if (playerId == Player1Id)
        {
            Player1Score = score;
        }
        else if (playerId == Player2Id)
        {
            Player2Score = score;
        }
    }

    /// <summary>
    /// Save game state snapshot.
    /// </summary>
    public void SaveSnapshot(byte[] snapshot)
    {
        GameStateSnapshot = snapshot;
    }

    /// <summary>
    /// Check if a player is a participant in this match.
    /// </summary>
    public bool IsParticipant(Guid playerId)
    {
        return playerId == Player1Id || playerId == Player2Id;
    }

    /// <summary>
    /// Get the opponent's player ID.
    /// </summary>
    public Guid GetOpponentId(Guid playerId)
    {
        if (playerId == Player1Id) return Player2Id;
        if (playerId == Player2Id) return Player1Id;
        throw new ArgumentException($"Player {playerId} is not in this match.");
    }

    /// <summary>
    /// Add an upgrade to the match for a specific player.
    /// </summary>
    public void AddUpgrade(Upgrade upgrade)
    {
        _upgrades.Add(upgrade);
    }

    /// <summary>
    /// Get upgrades for a specific player.
    /// </summary>
    public IEnumerable<Upgrade> GetPlayerUpgrades(Guid playerId)
    {
        return _upgrades.Where(u => u.OwnerId == playerId);
    }
}

/// <summary>
/// Represents player state within a match for validation purposes.
/// </summary>
public record MatchPlayerState(Guid PlayerId, IReadOnlyList<Upgrade> Upgrades);
