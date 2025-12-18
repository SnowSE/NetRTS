using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;

namespace NetRts.Domain.Entities;

/// <summary>
/// Pre-game area where players join and host configures match settings.
/// Aggregate root for lobby operations.
/// </summary>
public class MatchLobby
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid HostPlayerId { get; private set; }
    public LobbyStatus Status { get; private set; }
    public int MaxPlayers { get; private set; } = 2;
    public int CurrentPlayerCount => _players.Count;
    public GameSettings GameSettings { get; private set; } = GameSettings.Default;
    public DateTime CreatedAt { get; private set; }

    private readonly List<LobbyPlayer> _players = new();
    public IReadOnlyList<LobbyPlayer> Players => _players.AsReadOnly();

    private MatchLobby() { }

    public MatchLobby(string name, Guid hostPlayerId, GameSettings? settings = null)
    {
        Id = Guid.NewGuid();
        Name = name;
        HostPlayerId = hostPlayerId;
        Status = LobbyStatus.Open;
        GameSettings = settings ?? GameSettings.Default;
        CreatedAt = DateTime.UtcNow;
    }

    public void AddPlayer(Guid playerId, int slot)
    {
        if (_players.Count >= MaxPlayers)
        {
            throw new InvalidOperationException("Lobby is full.");
        }

        _players.Add(new LobbyPlayer(Id, playerId, slot));

        if (_players.Count == MaxPlayers)
        {
            Status = LobbyStatus.Full;
        }
    }

    public void RemovePlayer(Guid playerId)
    {
        _players.RemoveAll(p => p.PlayerId == playerId);
        if (Status == LobbyStatus.Full)
        {
            Status = LobbyStatus.Open;
        }
    }

    public void UpdateSettings(GameSettings settings)
    {
        if (Status != LobbyStatus.Open && Status != LobbyStatus.Full)
        {
            throw new InvalidOperationException("Cannot update settings after match starts.");
        }
        GameSettings = settings;
    }

    public void Start()
    {
        if (CurrentPlayerCount < MaxPlayers)
        {
            throw new InvalidOperationException("Cannot start match without all players.");
        }
        Status = LobbyStatus.Starting;
    }

    public void Close()
    {
        Status = LobbyStatus.Closed;
    }
}

/// <summary>
/// Represents a player in a lobby.
/// </summary>
public class LobbyPlayer
{
    public Guid LobbyId { get; private set; }
    public Guid PlayerId { get; private set; }
    public DateTime JoinedAt { get; private set; }
    public bool IsReady { get; private set; }
    public int Slot { get; private set; }

    private LobbyPlayer() { }

    public LobbyPlayer(Guid lobbyId, Guid playerId, int slot)
    {
        LobbyId = lobbyId;
        PlayerId = playerId;
        Slot = slot;
        JoinedAt = DateTime.UtcNow;
        IsReady = false;
    }

    public void SetReady(bool ready) => IsReady = ready;
}
