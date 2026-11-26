using System.Text.Json;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Audit;

/// <summary>
/// Rejestr audytowy na dysku: plik JSONL (<c>audit.jsonl</c>), dopisanie zdarzenia +
/// atomowa podmiana przy retencji. Audyt musi przeżyć restart serwera — pamięć
/// procesu nie wystarcza do rozliczalności, a RODO wymaga ograniczenia retencji
/// z tej samej strony: 12 miesięcy, potem wpisy znikają.
/// </summary>
public sealed class FileAuditLog : IAuditLog
{
    private readonly string _filePath;
    private readonly IClock _clock;
    private readonly TimeSpan _retention;
    private readonly TimeSpan _pruneInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    public FileAuditLog(string filePath, IClock clock, int retentionMonths = 12, TimeSpan? pruneInterval = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Ścieżka dziennika audytu jest wymagana.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
        _clock = clock;
        _retention = TimeSpan.FromDays(Math.Max(1, retentionMonths) * 30);
        _pruneInterval = pruneInterval ?? TimeSpan.FromHours(1);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PruneIfDueAsync(cancellationToken).ConfigureAwait(false);

            await using var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream);
            var line = JsonSerializer.Serialize(entry, ProtocolJson.Options);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(
        string? target,
        string? capability,
        int take,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await LoadAsync(cancellationToken).ConfigureAwait(false);

            IEnumerable<AuditEntry> filtered = all;

            if (!string.IsNullOrWhiteSpace(target))
            {
                filtered = filtered.Where(e =>
                    string.Equals(e.Target, target.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(capability))
            {
                filtered = filtered.Where(e =>
                    string.Equals(e.Capability, capability.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            return filtered
                .OrderByDescending(e => e.TimestampUtc)
                .Take(Math.Clamp(take, 1, 500))
                .ToList();
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
            var kept = all.Where(e => e.TimestampUtc >= cutoffUtc).ToList();
            var removed = all.Count - kept.Count;

            if (removed > 0)
            {
                await SaveAsync(kept, cancellationToken).ConfigureAwait(false);
            }

            _lastPrune = _clock.UtcNow;
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PruneIfDueAsync(CancellationToken cancellationToken)
    {
        if (_clock.UtcNow - _lastPrune < _pruneInterval)
        {
            return;
        }

        var cutoff = _clock.UtcNow - _retention;
        var all = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var kept = all.Where(e => e.TimestampUtc >= cutoff).ToList();

        if (kept.Count != all.Count)
        {
            await SaveAsync(kept, cancellationToken).ConfigureAwait(false);
        }

        _lastPrune = _clock.UtcNow;
    }

    private async Task<List<AuditEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new List<AuditEntry>();
        }

        var result = new List<AuditEntry>();
        var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken).ConfigureAwait(false);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<AuditEntry>(line, ProtocolJson.Options);
                if (entry is not null)
                {
                    result.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Pojedyncza uszkodzona linia nie unieważnia reszty audytu.
            }
        }

        return result;
    }

    private async Task SaveAsync(List<AuditEntry> entries, CancellationToken cancellationToken)
    {
        var temporary = _filePath + "." + Guid.NewGuid().ToString("n") + ".tmp";

        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await using var writer = new StreamWriter(stream);
            foreach (var entry in entries)
            {
                var line = JsonSerializer.Serialize(entry, ProtocolJson.Options);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        File.Move(temporary, _filePath, overwrite: true);
    }
}
