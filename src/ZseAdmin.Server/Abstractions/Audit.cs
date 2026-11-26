namespace ZseAdmin.Server.Abstractions;

/// <summary>
/// Wpis audytowy (docs/uprawnienia-i-dane-osobowe.md §4). Zapisywane są operacje
/// zmieniające stan oraz odrzucone próby dostępu — z aktorem, celem i wartością
/// przed/po. <see cref="Before"/>/<see cref="After"/> nie mogą zawierać sekretów
/// (hasła zapisujemy jako „(hasło utajnione)”).
/// </summary>
public sealed record AuditEntry(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string Actor,
    string Role,
    string Capability,
    string Target,
    string? Before,
    string? After,
    string Result,
    string? RemoteAddress);

/// <summary>
/// Rejestr audytowy. Retencja: 12 miesięcy. Implementacja: <c>FileAuditLog</c> —
/// ta sama semantyka co magazyn logowań (append + atomowa podmiana przy czyszczeniu).
/// </summary>
public interface IAuditLog
{
    /// <summary>Zapisuje wpis (atomowo). Błąd zapisu nie może być meldowany jako sukces operacji.</summary>
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Najnowsze wpisy (malejąco po czasie); opcjonalnie filtrowane po obiekcie i uprawnieniu.</summary>
    Task<IReadOnlyList<AuditEntry>> QueryAsync(
        string? target,
        string? capability,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Usuwa wpisy starsze niż <paramref name="cutoffUtc"/>. Zwraca liczbę usuniętych.</summary>
    Task<int> PruneAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
}
