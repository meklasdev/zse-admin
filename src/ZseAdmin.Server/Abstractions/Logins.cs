using ZseAdmin.Contracts.Protocol;

namespace ZseAdmin.Server.Abstractions;

/// <summary>Zapytanie do dziennika zabezpieczeń jednej stacji — tylko zdarzenia nie starsze niż <paramref name="NotBeforeUtc"/>.</summary>
public sealed record SecurityLogStationQuery(
    string ComputerName,
    string? ClassroomId,
    DateTimeOffset NotBeforeUtc);

/// <summary>Surowe zdarzenie z dziennika zabezpieczeń stacji (id zdarzenia i record id do deduplikacji).</summary>
public sealed record SecurityLogEvent(
    long RecordId,
    DateTimeOffset OccurredAtUtc,
    string UserSamAccountName,
    int EventId);

/// <summary>
/// Wynik odczytu dziennika jednej stacji. <see cref="Success"/> = false oznacza
/// brak dostępu (uprawnienia, sieć, stacja wyłączona) — nigdy cisza i pusta lista.
/// </summary>
public sealed record StationSecurityLogResult(
    string ComputerName,
    bool Success,
    string? Error,
    IReadOnlyList<SecurityLogEvent> Events);

/// <summary>Źródło zdarzeń logowań — zdalny odczyt dziennika zabezpieczeń (4624/4634/4625).</summary>
public interface ISecurityEventSource
{
    Task<IReadOnlyList<StationSecurityLogResult>> QueryStationsAsync(
        IReadOnlyList<SecurityLogStationQuery> queries,
        TimeSpan budget,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Magazyn zdarzeń logowań (etap M2): retencja 30 dni, deduplikacja po deterministycznym Id,
/// filtracja w źródle. Zapis jest kompletny poza <see cref="QueryAsync"/>, które zwraca
/// również <see cref="LoginQueryResult.TotalMatched"/> policzone po wszystkich filtrach.
/// </summary>
public interface ILoginEventStore
{
    /// <summary>Początek retencji (UTC); <c>null</c> = źródło nie deklaruje zakresu.</summary>
    DateTimeOffset? RetentionStartUtc { get; }

    /// <summary>
    /// Zdarzenia wg filtrów (take ≤ 500), malejąco po czasie. <paramref name="allowedClassroomIds"/>
    /// ogranicza widoczność (nauczyciel widzi wyłącznie swoje pracownie); zdarzenia bez
    /// przypisanej pracowni nie są wtedy widoczne.
    /// </summary>
    Task<LoginQueryResult> QueryAsync(
        LoginQueryRequest query,
        IReadOnlyCollection<string>? allowedClassroomIds,
        CancellationToken cancellationToken = default);

    /// <summary>Dodaje zdarzenia; identyczne (po Id) są ignorowane. Zwraca liczbę faktycznie dodanych.</summary>
    Task<int> AppendAsync(IReadOnlyList<LoginEventDto> events, CancellationToken cancellationToken = default);

    /// <summary>Usuwa zdarzenia starsze niż <paramref name="cutoffUtc"/>. Zwraca liczbę usuniętych.</summary>
    Task<int> PruneAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
}

/// <summary>Stan ostatniego cyklu zbierania — do metadanych kompletności („2 z 6 stacji bez dostępu”).</summary>
public sealed record LoginCollectionSnapshot(
    DateTimeOffset CompletedAtUtc,
    int StationsQueried,
    IReadOnlyList<string> FailedStations);

/// <summary>Współdzielony stan ostatniego zbierania logowań (collector zapisuje, LoginLog odczytuje).</summary>
public sealed class LoginCollectionState
{
    private readonly object _sync = new();
    private LoginCollectionSnapshot? _snapshot;

    public LoginCollectionSnapshot? Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public void Update(LoginCollectionSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshot = snapshot;
        }
    }

    /// <summary>Reset stanu (testy).</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _snapshot = null;
        }
    }
}
