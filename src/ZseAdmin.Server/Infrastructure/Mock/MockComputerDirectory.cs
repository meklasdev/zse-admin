using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Mock;

/// <summary>
/// In-memory inwentarz komputerów. <see cref="RefreshStatusesAsync"/> symuluje
/// sprawdzanie dostępności (ping) i zwraca wyłącznie komputery, których stan się zmienił.
/// </summary>
public sealed class MockComputerDirectory : IComputerDirectory
{
    private readonly object _sync = new();
    private readonly IClock _clock;
    private readonly Random _random = new(20260101);
    private readonly Dictionary<string, ComputerDto> _computers;

    public MockComputerDirectory(IClock clock)
    {
        _clock = clock;
        _computers = SchoolSeed.Computers(clock.UtcNow)
            .ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    public Task<ComputerQueryResult> ListAsync(string? classroomId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            IEnumerable<ComputerDto> query = _computers.Values;

            if (!string.IsNullOrWhiteSpace(classroomId))
            {
                query = query.Where(c => string.Equals(c.ClassroomId, classroomId, StringComparison.OrdinalIgnoreCase));
            }

            var items = query
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var observedAt = items
                .Where(c => c.StatusObservedAtUtc.HasValue)
                .Select(c => c.StatusObservedAtUtc)
                .DefaultIfEmpty()
                .Max();

            return Task.FromResult(new ComputerQueryResult(
                items,
                new QueryMetadata(observedAt, DataCompleteness.Complete, null)));
        }
    }

    public Task<IReadOnlyList<ComputerDto>> RefreshStatusesAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var changed = new List<ComputerDto>();
            var now = _clock.UtcNow;

            foreach (var name in _computers.Keys.ToList())
            {
                var computer = _computers[name];
                var roll = _random.Next(100);

                ComputerDto updated = roll switch
                {
    < 8 => computer with
    {
        Status = computer.Status == ComputerStatus.Online ? ComputerStatus.Offline : ComputerStatus.Online,
        StatusObservedAtUtc = now
    },
    < 22 => computer with { StatusObservedAtUtc = now },
    _ => computer
                };

                if (!ReferenceEquals(updated, computer))
                {
                    _computers[name] = updated;
                }

                if (updated.Status != computer.Status)
                {
                    changed.Add(updated);
                }
            }

            return Task.FromResult<IReadOnlyList<ComputerDto>>(changed);
        }
    }
}
