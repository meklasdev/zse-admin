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
/// <c>ClassroomId</c> jest dokładane przez serwer na podstawie inwentarza komputerów —
/// potrzebne do ograniczenia widoczności historii dla nauczyciela (M3).
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
/// Wpis audytu operacji (M3/M4). <c>Before</c>/<c>After</c> nigdy nie zawierają haseł —
/// wartości wrażliwe są maskowane przed zapisem.
/// </summary>
public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset TimestampUtc,
    string Actor,
    string Role,
    string Capability,
    string Action,
    string Target,
    string? Before,
    string? After,
    string Result,
    string? RemoteAddress,
    string? Detail);
