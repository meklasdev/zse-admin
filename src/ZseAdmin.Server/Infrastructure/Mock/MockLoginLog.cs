using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Mock;

/// <summary>
/// Historia logowań w pamięci (dane przykładowe). Filtrowanie i sortowanie odbywa się
/// tutaj, a nie w UI, bo w docelowej implementacji robi to źródło (magazyn zdarzeń).
/// Decyzja przypisana: <c>allowedClassroomIds</c> ogranicza widoczność nauczyciela (M3),
/// zdarzenia bez pracowni nie są wtedy widoczne.
/// </summary>
public sealed class MockLoginLog : ILoginLog
{
    private readonly IClock _clock;
    private readonly IReadOnlyList<LoginEventDto> _events;

    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(14);

    public MockLoginLog(IClock clock)
    {
        _clock = clock;
        _events = SchoolSeed.Logins(clock.UtcNow);
    }

    /// <summary>Dane przykładowe pokrywają ostatnie 14 dni — jak większość przewijanych dzienników.</summary>
    public DateTimeOffset? RetentionStartUtc => _clock.UtcNow - RetentionWindow;

    public Task<LoginQueryResult> QueryAsync(
        LoginQueryRequest query,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? allowedClassroomIds = null)
    {
        var now = _clock.UtcNow;
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

        if (!string.IsNullOrWhiteSpace(query.ClassroomId))
        {
            filtered = filtered.Where(e =>
                string.Equals(e.ClassroomId, query.ClassroomId, StringComparison.OrdinalIgnoreCase));
        }

        if (allowedClassroomIds is not null)
        {
            if (allowedClassroomIds.Count == 0)
            {
                filtered = Enumerable.Empty<LoginEventDto>();
            }
            else
            {
                var allowed = new HashSet<string>(allowedClassroomIds, StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(e => e.ClassroomId is not null && allowed.Contains(e.ClassroomId));
            }
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

        var take = query.Take <= 0 ? 100 : Math.Min(query.Take, 500);
        var page = materialized.Take(take).ToList();

        // MVP: źródło zwraca komplet danych z ostatnich 14 dni, ale nie gwarantuje
        // pełnej retencji dzienników — dlatego oznaczamy zakres jako częściowy.
        var metadata = new QueryMetadata(
            now,
            DataCompleteness.Partial,
            $"Źródło pokrywa ostatnie {RetentionWindow.TotalDays:0} dni.");

        return Task.FromResult(new LoginQueryResult(page, materialized.Count, metadata));
    }
}
