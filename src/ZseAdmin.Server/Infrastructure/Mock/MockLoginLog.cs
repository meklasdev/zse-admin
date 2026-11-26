using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Mock;

/// <summary>
/// Historia logowań w pamięci. Filtrowanie i sortowanie odbywa się tutaj, a nie w UI,
/// bo w docelowej implementacji zrobi to źródło (dziennik zdarzeń, baza).
/// </summary>
public sealed class MockLoginLog : ILoginLog
{
    private readonly IClock _clock;
    private readonly IReadOnlyList<LoginEventDto> _events;

    public MockLoginLog(IClock clock)
    {
        _clock = clock;
        _events = SchoolSeed.Logins(clock.UtcNow);
    }

    public Task<LoginQueryResult> QueryAsync(LoginQueryRequest query, CancellationToken cancellationToken = default)
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

        var take = query.Take <= 0 ? 100 : query.Take;
        var page = materialized.Take(take).ToList();

        // MVP: źródło zwraca komplet danych z ostatnich 14 dni, ale nie gwarantuje
        // pełnej retencji dzienników — dlatego oznaczamy zakres jako częściowy.
        var metadata = new QueryMetadata(now, DataCompleteness.Partial, "Źródło pokrywa ostatnie 14 dni.");

        return Task.FromResult(new LoginQueryResult(page, materialized.Count, metadata));
    }
}
