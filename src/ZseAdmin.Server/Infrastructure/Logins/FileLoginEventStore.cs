using System.Text.Json;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>
/// Magazyn zdarzeń logowań: plik JSONL (<c>logins.jsonl</c>), zapis przez dopisanie,
/// czyszczenie (retencja) przez atomową podmianę pliku. Zdarzenia u źródła są małe
/// (17–30 stacji, 4624/4634/4625), ale format jest celowo liniowy, żeby dopisanie
/// trzech zdarzeń nie przepisywało całego pliku.
///
/// Retencja jest odcinana po <see cref="LoginEventDto.OccurredAtUtc"/> (znacznikach czasu
/// zdarzeń), a nie po dacie pliku — przesunięte zegary stacji nie przedłużają retencji.
/// </summary>
public sealed class FileLoginEventStore : ILoginEventStore
{
    private readonly string _filePath;
    private readonly int _retentionDays;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileLoginEventStore(string filePath, int retentionDays, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Ścieżka magazynu logowań jest wymagana.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
        _retentionDays = Math.Max(1, retentionDays);
        _clock = clock;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public DateTimeOffset? RetentionStartUtc =>
        _clock.UtcNow - TimeSpan.FromDays(_retentionDays);

    public async Task<LoginQueryResult> QueryAsync(
        LoginQueryRequest query,
        IReadOnlyCollection<string>? allowedClassroomIds,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await LoadAsync(cancellationToken).ConfigureAwait(false);

            IEnumerable<LoginEventDto> filtered = all;

            if (!string.IsNullOrWhiteSpace(query.ComputerName))
            {
                filtered = filtered.Where(e =>
                    string.Equals(e.ComputerName, query.ComputerName.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query.UserSamAccountName))
            {
                filtered = filtered.Where(e =>
                    string.Equals(e.UserSamAccountName, query.UserSamAccountName.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query.ClassroomId))
            {
                filtered = filtered.Where(e =>
                    string.Equals(e.ClassroomId, query.ClassroomId.Trim(), StringComparison.OrdinalIgnoreCase));
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

            var ordered = filtered
                .OrderByDescending(e => e.OccurredAtUtc)
                .ToList();

            var take = query.Take <= 0 ? 100 : Math.Min(query.Take, 500);
            var page = ordered.Take(take).ToList();

            return new LoginQueryResult(
                page,
                ordered.Count,
                QueryMetadata.Complete(_clock.UtcNow));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> AppendAsync(IReadOnlyList<LoginEventDto> events, CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var known = new HashSet<Guid>(existing.Select(e => e.Id));
            var fresh = events
                .GroupBy(e => e.Id)
                .Select(g => g.First())
                .Where(e => known.Add(e.Id))
                .ToList();

            if (fresh.Count == 0)
            {
                return 0;
            }

            await using var stream = new FileStream(
                _filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);
            await using var writer = new StreamWriter(stream);

            foreach (var item in fresh)
            {
                var line = JsonSerializer.Serialize(item, ProtocolJson.Options);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            return fresh.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> PruneAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var kept = all.Where(e => e.OccurredAtUtc >= cutoffUtc).ToList();
            var removed = all.Count - kept.Count;

            if (removed > 0)
            {
                await SaveAsync(kept, cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Wczytuje zdarzenia; uszkodzone linie są pomijane (lepiej zgubić linię niż całą historię).</summary>
    private async Task<List<LoginEventDto>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new List<LoginEventDto>();
        }

        var result = new List<LoginEventDto>();
        var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken).ConfigureAwait(false);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var dto = JsonSerializer.Deserialize<LoginEventDto>(line, ProtocolJson.Options);
                if (dto is not null)
                {
                    result.Add(dto);
                }
            }
            catch (JsonException)
            {
                // Uszkodzona/nieskończona linia — pomijamy, nie wywracamy historii.
            }
        }

        return result;
    }

    private async Task SaveAsync(List<LoginEventDto> events, CancellationToken cancellationToken)
    {
        var temporary = _filePath + "." + Guid.NewGuid().ToString("n") + ".tmp";

        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await using var writer = new StreamWriter(stream);
            foreach (var item in events)
            {
                var line = JsonSerializer.Serialize(item, ProtocolJson.Options);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        File.Move(temporary, _filePath, overwrite: true);
    }
}
