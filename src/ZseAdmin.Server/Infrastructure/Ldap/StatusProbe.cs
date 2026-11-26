using System.Net.NetworkInformation;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Ldap;

/// <summary>
/// Sprawdza dostępność stacji przez ICMP (ping). Ograniczona równoległość chroni sieć szkolną
/// przed burzą pakietów, a wspólny budżet czasu — przed blokowaniem żądania klienta
/// dłużej niż jego timeout.
/// </summary>
/// <remarks>
/// Świadome ograniczenia (muszą być widoczne w UI — docs/model-danych.md §3):
/// ICMP bywa blokowany przez firewall, więc „offline” może znaczyć „zablokowany ping”.
/// Dlatego wynik jest zawsze parą (stan, czas obserwacji), a nie samą flagą.
/// </remarks>
public sealed class StatusProbe : IComputerStatusProbe
{
    private readonly StatusProbeOptions _options;

    public StatusProbe(StatusProbeOptions options)
    {
        _options = options;
    }

    public async Task<IReadOnlyDictionary<string, bool>> ProbeAsync(
        IReadOnlyCollection<string> hosts,
        TimeSpan budget,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        if (!_options.Enabled || hosts.Count == 0)
        {
            return results;
        }

        var timeout = Math.Clamp(_options.TimeoutMilliseconds, 100, 10_000);
        var parallelism = Math.Clamp(_options.MaxParallelism, 1, 128);

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (budget > TimeSpan.Zero)
        {
            budgetCts.CancelAfter(budget);
        }

        using var limiter = new SemaphoreSlim(parallelism, parallelism);
        var tasks = hosts
            .Select(host => ProbeOneAsync(host, timeout, limiter, results, budgetCts.Token))
            .ToList();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Budżet się skończył — zwracamy to, co zdążyliśmy zmierzyć.
            // Niesprawdzone stacje nie trafiają do wyniku („brak danych”, nie „offline”).
        }

        return results;
    }

    private static async Task ProbeOneAsync(
        string host,
        int timeout,
        SemaphoreSlim limiter,
        Dictionary<string, bool> results,
        CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeout).ConfigureAwait(false);

            lock (results)
            {
                results[host] = reply.Status == IPStatus.Success;
            }
        }
        catch (PingException)
        {
            lock (results)
            {
                results[host] = false;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Nieprawidłowa nazwa hosta — brak odpowiedzi, ale cykl monitorowania trwa dalej.
            lock (results)
            {
                results[host] = false;
            }
        }
        finally
        {
            limiter.Release();
        }
    }
}
