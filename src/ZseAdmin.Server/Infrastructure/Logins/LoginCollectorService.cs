using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Realtime;
using ZseAdmin.Server.Security;

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>
/// M2 — cykliczny odczyt dzienników 4624/4634/4625 do bufora i push nowych zdarzeń.
/// Interwał jest przycinany do ≤ 120 s, więc nowe logowanie pojawia się w kliencie
/// najpóźniej po 2 minutach nawet bez ręcznego odświeżenia.
/// </summary>
public sealed class LoginCollectorService : BackgroundService
{
    private readonly LoginHistoryBuffer _buffer;
    private readonly ConnectionRegistry _registry;
    private readonly ILoginAccessPolicy _accessPolicy;
    private readonly LoginHistoryOptions _options;
    private readonly ILogger<LoginCollectorService> _logger;

    public LoginCollectorService(
        LoginHistoryBuffer buffer,
        ConnectionRegistry registry,
        ILoginAccessPolicy accessPolicy,
        LoginHistoryOptions options,
        ILogger<LoginCollectorService> logger)
    {
        _buffer = buffer;
        _registry = registry;
        _accessPolicy = accessPolicy;
        _options = options;
        _logger = logger;
    }

    /// <summary>Widoczny dla testów: gwarantuje kryterium „≤ 2 minuty”.</summary>
    public static TimeSpan ClampInterval(int pollSeconds) =>
        TimeSpan.FromSeconds(Math.Clamp(pollSeconds <= 0 ? 60 : pollSeconds, 10, 120));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = ClampInterval(_options.PollSeconds);
        using var timer = new PeriodicTimer(interval);

        // Pierwszy odczyt od razu po starcie — pusta historia nie czeka minutę.
        await PollOnceAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd cyklu pobierania historii logowań.");
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LoginEventDto> added;
        try
        {
            added = await _buffer.IngestAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _buffer.MarkUnavailable(ex.Message);
            _logger.LogError(ex, "Źródło historii logowań nie odpowiedziało.");
            return;
        }

        if (added.Count == 0)
        {
            return;
        }

        // Jedna koperta na sesję, zbudowana z jej własnego zakresu pracowni (M3).
        await _registry.BroadcastAsync(
                identity =>
                {
                    var visible = added
                        .Where(e => _accessPolicy.CanSeeEvent(identity, e))
                        .Take(50)
                        .ToList();

                    return visible.Count == 0
                        ? null
                        : WsEnvelope.Create(
                            MessageTypes.LoginEventsEvent,
                            null,
                            new LoginEventsChangedEvent(visible));
                },
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Dopisano {Count} nowych zdarzeń logowań do bufora.", added.Count);
    }
}
