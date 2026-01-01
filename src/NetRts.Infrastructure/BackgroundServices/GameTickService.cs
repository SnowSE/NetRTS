using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetRts.Application.Services;

namespace NetRts.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that processes game ticks at 1-second intervals.
/// </summary>
public class GameTickService : BackgroundService
{
    private readonly ILogger<GameTickService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _tickInterval;

    public GameTickService(
        ILogger<GameTickService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _tickInterval = TimeSpan.FromSeconds(1); // 1-second ticks
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Game Tick Service starting");

        using var timer = new PeriodicTimer(_tickInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tickProcessor = scope.ServiceProvider.GetRequiredService<IGameTickProcessor>();
                await tickProcessor.ProcessTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing game tick");
            }
        }

        _logger.LogInformation("Game Tick Service stopping");
    }
}
