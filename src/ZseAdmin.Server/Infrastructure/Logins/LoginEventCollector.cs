using Microsoft.Extensions.Logging;
using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>Wynik jednego cyklu zbierania zdarzeń.</summary>
public sealed record LoginCollectionOutcome(
    int StationsQueried,
    int StationsFailed,
    int EventsAdded,
    int EventsPruned);

/// <summary>
/// Jeden cykl zbierania zdarzeń logowań ze stacji (etap M2):
/// stacje z inwentarza → dzienniki 4624/4634/4625 → mapowanie na DTO
/// (z przypisaniem do pracowni) → magazyn lokalny → retencja 30 dni →
/// stan cyklu (do noty „brak dostępu na N z M stacji”).
/// </summary>
public sealed class LoginEventCollector
{
    private readonly IComputerDirectory _computers;
    private readonly ISecurityEventSource _source;
    private readonly ILoginEventStore _store;
    private readonly LoginCollectionState _state;
    private readonly LoginsOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<LoginEventCollector> _logger;

    public LoginEventCollector(
        IComputerDirectory computers,
        ISecurityEventSource source,
        ILoginEventStore store,
        LoginCollectionState state,
        LoginsOptions options,
        IClock clock,
        ILogger<LoginEventCollector> logger)
    {
        _computers = computers;
        _source = source;
        _store = store;
        _state = state;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<LoginCollectionOutcome> CollectOnceAsync(CancellationToken cancellationToken = default)
    {
        var inventory = await _computers.ListAsync(null, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var notBefore = now - TimeSpan.FromDays(Math.Max(1, _options.RetentionDays));

        var queries = inventory.Items
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new SecurityLogStationQuery(c.Name, c.ClassroomId, notBefore))
            .ToList();

        if (queries.Count == 0)
        {
            _state.Update(new LoginCollectionSnapshot(now, 0, Array.Empty<string>()));
            _logger.LogWarning("Brak stacji do odczytu dzienników — sprawdź inwentarz AD.");
            return new LoginCollectionOutcome(0, 0, 0, 0);
        }

        var budget = TimeSpan.FromSeconds(Math.Clamp(_options.StationAccessBudgetSeconds, 1, 300));
        var results = await _source.QueryStationsAsync(queries, budget, cancellationToken).ConfigureAwait(false);

        var classroomByComputer = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in inventory.Items)
        {
            classroomByComputer[item.Name] = item.ClassroomId;
        }

        var events = new List<LoginEventDto>();
        var failed = new List<string>();

        foreach (var station in results)
        {
            if (!station.Success)
            {
                failed.Add(station.ComputerName);
                _logger.LogWarning(
                    "Stacja {Computer}: brak dostępu do dziennika — {Reason}",
                    station.ComputerName,
                    station.Error ?? "nieznany błąd");
                continue;
            }

            classroomByComputer.TryGetValue(station.ComputerName, out var classroomId);

            foreach (var entry in station.Events)
            {
                var mapped = SecurityEventMapper.Map(entry, station.ComputerName, classroomId);
                if (mapped is not null)
                {
                    events.Add(mapped);
                }
            }
        }

        var added = await _store.AppendAsync(events, cancellationToken).ConfigureAwait(false);
        var pruned = await _store.PruneAsync(notBefore, cancellationToken).ConfigureAwait(false);

        var failedNames = failed
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _state.Update(new LoginCollectionSnapshot(_clock.UtcNow, queries.Count, failedNames));

        _logger.LogInformation(
            "Zbiór logowań: {Added} nowych zdarzeń, {EventsInScope} odczytanych, {Failed}/{Total} stacji bez dostępu, {Pruned} usuniętych (retencja {RetentionDays} dni).",
            added,
            events.Count,
            failedNames.Count,
            queries.Count,
            pruned,
            _options.RetentionDays);

        return new LoginCollectionOutcome(queries.Count, failedNames.Count, added, pruned);
    }
}
