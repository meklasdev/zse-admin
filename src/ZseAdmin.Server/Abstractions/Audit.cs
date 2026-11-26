using ZseAdmin.Contracts;

namespace ZseAdmin.Server.Abstractions;

/// <summary>
/// Dane jednego wpisu audytu. Hasła nigdy nie są przekazywane w <c>Before</c>/<c>After</c> —
/// zapis do AD maskuje je wcześniej.
/// </summary>
public sealed record AuditRecord(
    string Actor,
    string Role,
    string Capability,
    string Action,
    string Target,
    string? Before,
    string? After,
    string Result,
    string? RemoteAddress,
    string? Detail = null);

public sealed record AuditQueryResult(
    IReadOnlyList<AuditEntryDto> Items,
    int TotalMatched,
    QueryMetadata Metadata);

/// <summary>Rejestr operacji (M3) i zmian (M4). Retencja: 12 miesięcy.</summary>
public interface IAuditLog
{
    DateTimeOffset? RetentionStartUtc { get; }

    Task<AuditEntryDto> RecordAsync(AuditRecord record, CancellationToken cancellationToken = default);

    Task<AuditQueryResult> QueryAsync(string? target, int take, CancellationToken cancellationToken = default);
}
