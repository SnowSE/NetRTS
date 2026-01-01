using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetRts.Application.Interfaces;
using NetRts.Domain.Enums;

namespace NetRts.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that creates periodic snapshots of active matches for crash recovery.
/// Snapshots are created every 10 ticks.
/// </summary>
public class MatchSnapshotService : BackgroundService
{
    private readonly ILogger<MatchSnapshotService> _logger;
    private readonly IGameStateCache _gameStateCache;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _snapshotInterval;
    private const int TicksPerSnapshot = 10;

    public MatchSnapshotService(
        ILogger<MatchSnapshotService> logger,
        IGameStateCache gameStateCache,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _gameStateCache = gameStateCache;
        _serviceProvider = serviceProvider;
        _snapshotInterval = TimeSpan.FromSeconds(TicksPerSnapshot); // 10 seconds (10 ticks at 1 tick/second)
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Match Snapshot Service starting");

        using var timer = new PeriodicTimer(_snapshotInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CreateSnapshotsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating match snapshots");
            }
        }

        _logger.LogInformation("Match Snapshot Service stopping");
    }

    private async Task CreateSnapshotsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var matchRepository = scope.ServiceProvider.GetRequiredService<IMatchRepository>();

        // Get all active matches from cache
        var activeMatches = await matchRepository.GetActiveMatchesAsync(cancellationToken);

        foreach (var match in activeMatches)
        {
            try
            {
                // Only snapshot if match tick is a multiple of 10
                if (match.CurrentTick % TicksPerSnapshot != 0)
                {
                    continue;
                }

                // Get match from cache
                var cachedMatch = _gameStateCache.GetMatch(match.Id);
                if (cachedMatch == null)
                {
                    continue;
                }

                // Update snapshot in database
                await matchRepository.UpdateAsync(cachedMatch, cancellationToken);

                _logger.LogInformation(
                    "Created snapshot for match {MatchId} at tick {CurrentTick}",
                    match.Id,
                    match.CurrentTick);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error creating snapshot for match {MatchId}",
                    match.Id);
            }
        }
    }
}
