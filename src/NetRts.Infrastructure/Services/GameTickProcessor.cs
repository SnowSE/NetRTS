using Microsoft.Extensions.Logging;
using NetRts.Application.Interfaces;
using NetRts.Application.Services;
using NetRts.Domain.Enums;
using NetRts.Infrastructure.Caching;

namespace NetRts.Infrastructure.Services;

public class GameTickProcessor : IGameTickProcessor
{
    private readonly ILogger<GameTickProcessor> _logger;
    private readonly GameStateCache _gameStateCache;
    private readonly IMatchRepository _matchRepository;
    private readonly ICommandQueueManager _commandQueueManager;

    public GameTickProcessor(
        ILogger<GameTickProcessor> logger,
        GameStateCache gameStateCache,
        IMatchRepository matchRepository,
        ICommandQueueManager commandQueueManager)
    {
        _logger = logger;
        _gameStateCache = gameStateCache;
        _matchRepository = matchRepository;
        _commandQueueManager = commandQueueManager;
    }

    public async Task ProcessTickAsync(CancellationToken cancellationToken = default)
    {
        var activeMatches = _gameStateCache.GetAll()
            .Where(m => m.Status == MatchStatus.Active)
            .ToList();

        _logger.LogDebug("Processing tick for {MatchCount} active matches", activeMatches.Count);

        foreach (var match in activeMatches)
        {
            try
            {
                await ProcessMatchTickAsync(match.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing tick for match {MatchId}", match.Id);
            }
        }
    }

    public async Task ProcessMatchTickAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var match = _gameStateCache.Get(matchId);
        if (match == null || match.Status != MatchStatus.Active)
        {
            return;
        }

        // Advance tick
        match.AdvanceTick();

        // Process commands for each player
        await ProcessPlayerCommands(match, match.Player1Id, cancellationToken);
        await ProcessPlayerCommands(match, match.Player2Id, cancellationToken);

        // Update game state cache
        _gameStateCache.AddOrUpdate(match);

        // Periodic snapshot to database (every 10 ticks)
        if (match.CurrentTick % 10 == 0)
        {
            await _matchRepository.UpdateAsync(match, cancellationToken);
        }
    }

    private async Task ProcessPlayerCommands(
        Domain.Entities.Match match,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        // Dequeue commands for this player
        var commands = await _commandQueueManager.DequeueCommandsAsync(
            match.Id,
            playerId,
            match.CommandsPerTick);

        foreach (var command in commands)
        {
            // TODO: Execute command logic
            // For now, just mark as executed
            command.MarkExecuted(match.CurrentTick);
        }
    }
}
