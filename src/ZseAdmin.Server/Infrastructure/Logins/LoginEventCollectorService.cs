using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>
/// Cykliczne zbieranie zdarzeń logowań. Pierwszy cykl startuje od razu po starcie
/// serwera (zdarzenia mają być widoczne w ≤ 2 minuty), potem co <c>ZseAdmin:Logins:PollSeconds</c>.
/// </summary>
public sealed class LoginEventCollectorService : BackgroundService
{
    private readonly LoginEventCollector _collector;
    private readonly ZseAdminOptions _options;
    private readonly ILogger<LoginEventCollectorService> _logger;

    public LoginEventCollectorService(
        LoginEventCollector collector,
        ZseAdminOptions options,
        ILogger<LoginEventCollectorService> logger)
    {
        _collector = collector;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TryCollectAsync(stoppingToken).ConfigureAwait(false);

        var interval = TimeSpan.FromSeconds(Math.Max(15, _options.Logins.PollSeconds));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await TryCollectAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task TryCollectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _collector.CollectOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Brak dostępu do sieci/DC nie może wywrócić serwera — świeżość danych
            // pokaże czas poprzedniego udanego cyklu (lub „niedostępne”).
            _logger.LogError(ex, "Cykl zbierania logowań nie powiódł się; ponowna próba w kolejnym oknie.");
        }
    }
}
