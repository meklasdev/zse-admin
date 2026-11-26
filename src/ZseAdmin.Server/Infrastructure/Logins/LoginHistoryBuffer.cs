using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>
/// M2 — lokalny bufor zdarzeń logowań. Źródło (WEF lub zdalny dziennik Security) jest
/// odpytywane cyklicznie; bufor deduplikuje nakładające się odczyty, pilnuje retencji
/// 30 dni i opisuje kompletność na podstawie dostępności poszczególnych stacji.
/// Brak danych nigdy nie wygląda jak „nikt się nie logował” (ADR-0006).
/// </summary>
public sealed class LoginHistoryBuffer : ILoginLog
{
    private readonly object _sync = new();
    private readonly ILoginEventSource _source;
    private readonly LoginHistoryOptions _options;
    private readonly IClock _clock;
    private readonly List<LoginEventDto> _events = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StationLogStatus> _stations = new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset? _lastPollUtc;
    private DateTimeOffset? _lastSuccessUtc;
    private string? _sourceFailure;
    private bool _trimmed;

    public LoginHistoryBuffer(
        ILoginEventSource source,
        LoginHistoryOptions options,
        IClock clock)
    {
        _source = source;
        _options = options;
        _clock = clock;
    }

    /// <summary>Początek 30-dniowej retencji (liczony na bieżąco, testowalny zegarem).</summary>
    public DateTimeOffset? RetentionStartUtc =>
        _clock.UtcNow.AddDays(-Math.Max(1, _options.RetentionDays));

    /// <summary>Aktualna liczba zdarzeń w buforze (po retencji) — używana w testach.</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _events.Count;
            }
        }
    }

    public int StationCount
    {
        get
        {
            lock (_sync)
            {
                return _stations.Count;
            }
        }
    }

    /// <summary>
    /// Pobiera jedną porcję ze źródła. Zwraca wyłącznie nowe zdarzenia — kolektor
    /// rozgłasza je jako <c>event.logins.new</c>, więc aplikacja widzi logowanie
    /// bez odświeżania ręcznego (kryterium M2: ≤ 2 minuty).
    /// </summary>
    public async Task<IReadOnlyList<LoginEventDto>> IngestAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var lookback = TimeSpan.FromMinutes(Math.Clamp(_options.LookbackMinutes, 1, 24 * 60));
        var overlap = TimeSpan.FromMinutes(Math.Clamp(_options.OverlapMinutes, 0, 60));
        var since = (_lastSuccessUtc ?? now - lookback) - overlap;

        var batch = await _source.ReadSinceAsync(since, cancellationToken).ConfigureAwait(false);
        var added = new List<LoginEventDto>();

        lock (_sync)
        {
            var retentionStart = RetentionStartUtc!.Value;

            foreach (var loginEvent in batch.Events)
            {
                // Odrzucamy dane z przyszłości (skrzywiony zegar stacji) i już wygasłe.
                if (loginEvent.OccurredAtUtc > now.AddMinutes(5) ||
                    loginEvent.OccurredAtUtc < retentionStart)
                {
                    continue;
                }

                if (_seen.Add(DedupKey(loginEvent)))
                {
                    _events.Add(loginEvent);
                    added.Add(loginEvent);
                }
            }

            foreach (var station in batch.Stations)
            {
                _stations[station.ComputerName] = station;
            }

            _lastPollUtc = now;
            _lastSuccessUtc = now;
            _sourceFailure = null;
            Prune();
            Trim();
        }

        return added;
    }

    /// <summary>Gdy całe źródło zawiedzie, zaznaczamy odpowiedź jako niedostępną.</summary>
    public void MarkUnavailable(string reason)
    {
        lock (_sync)
        {
            _lastPollUtc = _clock.UtcNow;
            _sourceFailure = reason;
        }
    }

    public Task<LoginQueryResult> QueryAsync(
        LoginQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var take = Math.Clamp(query.Take <= 0 ? 100 : query.Take, 1, 500);
        var now = _clock.UtcNow;

        lock (_sync)
        {
            Prune();

            IEnumerable<LoginEventDto> filtered = _events;

            if (!string.IsNullOrWhiteSpace(query.ComputerName))
            {
                filtered = filtered.Where(e =>
                    string.Equals(e.ComputerName, query.ComputerName, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query.UserSamAccountName))
            {
                filtered = filtered.Where(e =>
                    string.Equals(e.UserSamAccountName, query.UserSamAccountName, StringComparison.OrdinalIgnoreCase));
            }

            var classroomIds = BuildClassroomFilter(query);
            if (classroomIds is { Count: > 0 })
            {
                filtered = filtered.Where(e =>
                    e.ClassroomId is { Length: > 0 } id &&
                    classroomIds.Any(c => string.Equals(c, id, StringComparison.OrdinalIgnoreCase)));
            }

            if (query.FromUtc is { } from)
            {
                filtered = filtered.Where(e => e.OccurredAtUtc >= from);
            }

            if (query.ToUtc is { } to)
            {
                filtered = filtered.Where(e => e.OccurredAtUtc <= to);
            }

            var materialized = filtered
                .OrderByDescending(e => e.OccurredAtUtc)
                .ToList();

            var page = materialized.Take(take).ToList();
            var metadata = BuildMetadata(now, query);

            return Task.FromResult(new LoginQueryResult(page, materialized.Count, metadata));
        }
    }

    private QueryMetadata BuildMetadata(DateTimeOffset now, LoginQueryRequest query)
    {
        if (_sourceFailure is { Length: > 0 } failure)
        {
            return QueryMetadata.Unavailable($"Źródło historii logowań nieosiągalne: {failure}");
        }

        if (_lastPollUtc is not { } observedAt)
        {
            return QueryMetadata.Unavailable(
                "Bufor historii logowań nie pobrał jeszcze danych ze źródła — brak zdarzeń nie oznacza braku logowań.");
        }

        var unavailableStations = _stations.Values.Where(s => !s.Available).ToList();
        var notes = new List<string>();

        QueryMetadata metadata;
        if (unavailableStations.Count > 0)
        {
            var names = string.Join(", ", unavailableStations.Select(s => s.ComputerName));
            notes.Add(
                $"Brak dostępu do dziennika na {unavailableStations.Count} z {_stations.Count} stacji: {names}.");

            var reasons = unavailableStations
                .Where(s => !string.IsNullOrWhiteSpace(s.Reason))
                .Select(s => $"{s.ComputerName}: {s.Reason}")
                .Take(2)
                .ToList();

            if (reasons.Count > 0)
            {
                notes.Add(string.Join("; ", reasons));
            }

            metadata = QueryMetadata.Partial(observedAt, string.Join(" ", notes));
        }
        else if (_stations.Count == 0)
        {
            metadata = QueryMetadata.Partial(
                observedAt, "Źródło nie zadeklarowało żadnej stacji — kompletność niepotwierdzona.");
        }
        else
        {
            metadata = QueryMetadata.Complete(observedAt);
        }

        if (_trimmed)
        {
            notes.Add($"Bufor osiągnął limit {Math.Max(100, _options.MaxEvents)} zdarzeń — najstarsze usunięte.");
        }

        // Zakres przed retencją: jawny komunikat, nigdy pusta lista bez wyjaśnienia.
        if (query.FromUtc is { } from && RetentionStartUtc is { } retentionStart && from < retentionStart)
        {
            notes.Add(
                $"Żądany zakres wykracza poza retencję {Math.Max(1, _options.RetentionDays)} dni " +
                $"(dane od {retentionStart:yyyy-MM-dd HH:mm} UTC).");
        }

        if (notes.Count == 0)
        {
            return metadata;
        }

        var note = string.Join(" ", notes.Distinct(StringComparer.Ordinal));
        var completeness = metadata.Completeness == DataCompleteness.Unavailable
            ? DataCompleteness.Unavailable
            : DataCompleteness.Partial;

        return new QueryMetadata(metadata.ObservedAtUtc ?? now, completeness, note);
    }

    private static IReadOnlyList<string>? BuildClassroomFilter(LoginQueryRequest query)
    {
        if (!string.IsNullOrWhiteSpace(query.ClassroomId))
        {
            return new[] { query.ClassroomId.Trim() };
        }

        return query.ClassroomIds is { Count: > 0 } ids
            ? ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList()
            : null;
    }

    private void Prune()
    {
        var retentionStart = RetentionStartUtc!.Value;
        _events.RemoveAll(e => e.OccurredAtUtc < retentionStart);

        // Klucze odbudowujemy z aktualnego stanu: zdarzenie wygasłe nie blokuje
        // ponownego wczytania, a bufor nie rośnie w nieskończoność.
        _seen.Clear();
        foreach (var loginEvent in _events)
        {
            _seen.Add(DedupKey(loginEvent));
        }
    }

    private void Trim()
    {
        var limit = Math.Max(100, _options.MaxEvents);
        if (_events.Count <= limit)
        {
            return;
        }

        var newest = _events
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(limit)
            .ToList();

        _events.Clear();
        _events.AddRange(newest);
        _trimmed = true;

        _seen.Clear();
        foreach (var loginEvent in _events)
        {
            _seen.Add(DedupKey(loginEvent));
        }
    }

    private static string DedupKey(LoginEventDto e) =>
        $"{e.ComputerName.ToUpperInvariant()}|{e.UserSamAccountName.ToUpperInvariant()}|" +
        $"{e.OccurredAtUtc.UtcTicks}|{(int)e.Type}";
}
