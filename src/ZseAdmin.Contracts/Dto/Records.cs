namespace ZseAdmin.Contracts;

/// <summary>
/// Pracownia — szkolny, fizyczny kontekst. Nie jest to to samo co OU w Active Directory,
/// dlatego OU jest opcjonalnym, jawnie mapowanym atrybutem.
/// </summary>
public sealed record ClassroomDto(
    string Id,
    string Name,
    string? OuDistinguishedName,
    int ComputerCount);

/// <summary>
/// Komputer szkolny. Stan i IP są informacją bieżącą lub ostatnio zaobserwowaną,
/// dlatego zawsze towarzyszy im znacznik czasu obserwacji.
/// </summary>
public sealed record ComputerDto(
    string Name,
    string? ClassroomId,
    string? ClassroomName,
    string? OuDistinguishedName,
    string? IpAddress,
    ComputerStatus Status,
    DateTimeOffset? StatusObservedAtUtc,
    DateTimeOffset? LastLogonAtUtc,
    string? OperatingSystem);

/// <summary>
/// Pojedyncze zdarzenie logowania. Historia zdarzeniowa, nie lista aktywnych sesji.
/// <see cref="ClassroomId"/> jest denormalizowany przy zbiorze (etap M2) i służy m.in.
/// do ograniczenia widoczności historii dla nauczycieli (etap M3).
/// </summary>
public sealed record LoginEventDto(
    Guid Id,
    string UserSamAccountName,
    string? UserDisplayName,
    string ComputerName,
    DateTimeOffset OccurredAtUtc,
    LoginEventType Type,
    string Source,
    string? ClassroomId = null);

/// <summary>Konto AD wraz z przynależnością do grup.</summary>
public sealed record AdUserDto(
    string SamAccountName,
    string? DisplayName,
    string? Email,
    bool Enabled,
    IReadOnlyList<string> Groups,
    string? OuDistinguishedName,
    DateTimeOffset? LastLogonAtUtc);

/// <summary>
/// Metadane zaufania do danych: kiedy je zaobserwowano, czy są świeże
/// i czy są kompletne. Bez tego UI nie powinien twierdzić, że dane są aktualne.
/// </summary>
public sealed record DataFreshnessDto(
    string Source,
    DateTimeOffset? ObservedAtUtc,
    bool IsStale,
    DataCompleteness Completeness,
    string? Note);

/// <summary>
/// Wpis audytowy (etap M4): kto, kiedy, na jakim obiekcie, co się zmieniło przed i po,
/// z jakim wynikiem. <see cref="Before"/>/<see cref="After"/> nigdy nie zawierają sekretów
/// (hasło jest zapisywane jako „(hasło utajnione)”). Retencja: 12 miesięcy.
/// </summary>
public sealed record AuditEntryDto(
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
