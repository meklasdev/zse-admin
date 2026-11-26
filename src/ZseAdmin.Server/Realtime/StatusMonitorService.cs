using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Security;

namespace ZseAdmin.Server.Realtime;

/// <summary>
/// Cyklicznie odświeża stan komputerów i rozgłasza tylko faktyczne zmiany.
/// Brak zdarzenia nie oznacza braku zmiany — klient i tak odświeża się na żądanie,
/// a UI pokazuje czas obserwacji.
/// </summary>
public sealed class StatusMonitorService : BackgroundService
{
    private readonly IComputerDirectory _computers;
    private readonly ConnectionRegistry _registry;
    private readonly ZseAdminOptions _options;
    private readonly ILogger<StatusMonitorService> _logger;

    public StatusMonitorService(
        IComputerDirectory computers,
        ConnectionRegistry registry,
        ZseAdminOptions options,
        ILogger<StatusMonitorService> logger)
    {
        _computers = computers;
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.StatusPollSeconds));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                var changed = await _computers.RefreshStatusesAsync(stoppingToken);
                if (changed.Count == 0)
                {
                    continue;
                }

                foreach (var computer in changed)
                {
                    var envelope = WsEnvelope.Create(
                        MessageTypes.ComputerStatusEvent,
                        null,
                        new ComputerStatusChangedEvent(computer));

                    await _registry.BroadcastAsync(envelope, Capability.ComputersRead, stoppingToken);
                }

                _logger.LogInformation("Rozgłoszono {Count} zmian stanu komputerów.", changed.Count);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd monitorowania stanu komputerów.");
            }
        }
    }
}
