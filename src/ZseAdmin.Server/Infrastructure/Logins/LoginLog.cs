using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>
/// Historia logowań (M2): odczyt z lokalnego magazynu zretencjonowanego do
/// <c>ZseAdmin:Logins:RetentionDays</c> (domyślnie 30 dni).
///
/// Kompletność odpowiedzi wynika z ostatniego cyklu zbierania, a nie z samego pliku:
/// gdy 2 z 6 stacji nie udostępnią dziennika — <c>completeness = partial</c> i nota
/// z liczbą i nazwami stacji. Pusty wynik nigdy nie jest jedyną informacją.
/// </summary>
public sealed class LoginLog : ILoginLog
{
    private readonly ILoginEventStore _store;
    private readonly LoginCollectionState _state;
    private readonly LoginsOptions _options;
    private readonly IClock _clock;

    public LoginLog(
        ILoginEventStore store,
        LoginCollectionState state,
        LoginsOptions options,
        IClock clock)
    {
        _store = store;
        _state = state;
        _options = options;
        _clock = clock;
    }

    public DateTimeOffset? RetentionStartUtc =>
        _clock.UtcNow - TimeSpan.FromDays(Math.Max(1, _options.RetentionDays));

    public async Task<LoginQueryResult> QueryAsync(
        LoginQueryRequest query,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? allowedClassroomIds = null)
    {
        LoginQueryResult result;
        try
        {
            result = await _store.QueryAsync(query, allowedClassroomIds, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new SourceUnavailableException(
                "Magazyn historii logowań",
                $"Nie można odczytać lokalnego magazynu zdarzeń: {ex.Message}",
                ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new SourceUnavailableException(
                "Magazyn historii logowań",
                $"Nie można przetworzyć lokalnego magazynu zdarzeń: {ex.Message}",
                ex);
        }

        return result with { Metadata = BuildMetadata() };
    }

    private QueryMetadata BuildMetadata()
    {
        var snapshot = _state.Snapshot;

        if (snapshot is null)
        {
            return QueryMetadata.Unavailable(
                "Dzienniki stacji nie zostały jeszcze odczytane (pierwsze zbieranie w toku).");
        }

        if (snapshot.FailedStations.Count == 0)
        {
            return QueryMetadata.Complete(snapshot.CompletedAtUtc);
        }

        var names = string.Join(", ", snapshot.FailedStations
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(10));
        var suffix = snapshot.FailedStations.Count > 10
            ? $" (+{snapshot.FailedStations.Count - 10})"
            : string.Empty;

        return QueryMetadata.Partial(
            snapshot.CompletedAtUtc,
            $"Brak dostępu do dziennika na {snapshot.FailedStations.Count} z {snapshot.StationsQueried} stacji ({names}{suffix}).");
    }
}
