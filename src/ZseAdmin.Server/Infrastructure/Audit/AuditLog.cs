using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Audit;

/// <summary>
/// Rejestr audytu operacji (M3/M4): aktor, rola, uprawnienie, cel, wartość przed/po,
/// wynik i adres zdalny. Domyślnie w pamięci; opcjonalnie dopisuje wpisy do pliku JSONL,
/// żeby restart serwera nie usuwał rozliczalności. Retencja 12 miesięcy.
/// </summary>
public sealed class AuditLog : IAuditLog
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly List<AuditEntryDto> _entries = new();
    private readonly IClock _clock;
    private readonly AuditOptions _options;
    private readonly ILogger<AuditLog> _logger;

    public AuditLog(IClock clock, AuditOptions options, ILogger<AuditLog> logger)
    {
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    /// <summary>Ostatni błąd dopisywania do pliku (do /healthz i diagnostyki wdrożenia).</summary>
    public string? LastPersistenceError { get; private set; }

    public DateTimeOffset? RetentionStartUtc =>
        _clock.UtcNow.AddMonths(-Math.Max(1, _options.RetentionMonths));

    public async Task<AuditEntryDto> RecordAsync(
        AuditRecord record,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntryDto(
            Guid.NewGuid(),
            _clock.UtcNow,
            record.Actor,
            record.Role,
            record.Capability,
            record.Action,
            record.Target,
            record.Before,
            record.After,
            record.Result,
            record.RemoteAddress,
            record.Detail);

        lock (_sync)
        {
            _entries.Add(entry);
            Prune();
        }

        if (!string.IsNullOrWhiteSpace(_options.FilePath))
        {
            await AppendToFileAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        return entry;
    }

    public Task<AuditQueryResult> QueryAsync(
        string? target,
        int take,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limit = Math.Clamp(take <= 0 ? 50 : take, 1, 500);

        lock (_sync)
        {
            Prune();

            IEnumerable<AuditEntryDto> query = _entries;
            if (!string.IsNullOrWhiteSpace(target))
            {
                query = query.Where(e => e.Target.Contains(target.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            var materialized = query
                .OrderByDescending(e => e.TimestampUtc)
                .ToList();

            var page = materialized.Take(limit).ToList();
            var metadata = QueryMetadata.Complete(_clock.UtcNow);

            return Task.FromResult(new AuditQueryResult(page, materialized.Count, metadata));
        }
    }

    private void Prune()
    {
        var start = RetentionStartUtc;
        if (start is null)
        {
            return;
        }

        _entries.RemoveAll(e => e.TimestampUtc < start.Value);
    }

    private async Task AppendToFileAsync(AuditEntryDto entry, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = Path.GetFullPath(_options.FilePath!);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = JsonSerializer.Serialize(entry, ProtocolJson.Options);
            await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
            LastPersistenceError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Awaria pliku nie może zatrzymać akcji — wpis i tak jest w pamięci, a błąd
            // jest widoczny dla administratora (i w logu serwera).
            LastPersistenceError = ex.Message;
            _logger.LogWarning(ex, "Nie udało się dopisać wpisu audytu do {Path}.", _options.FilePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
