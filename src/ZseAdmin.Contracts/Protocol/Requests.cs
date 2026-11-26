namespace ZseAdmin.Contracts.Protocol;

/// <summary>Payloady żądań wysyłanych przez klienta.</summary>
public sealed record ComputerListRequest(
    string? ClassroomId = null,
    bool IncludeOffline = true);

/// <summary>
/// Zakres zapytania o historię logowań. Czasy są w UTC.
/// <see cref="ClassroomId"/> (dodane w M2/M3) filtruje zdarzenia po pracowni; nauczyciel
/// może pytać wyłącznie o pracownie przypisane do jego konta.
/// </summary>
public sealed record LoginQueryRequest(
    string? ComputerName = null,
    string? UserSamAccountName = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Take = 100,
    string? ClassroomId = null);

public sealed record UserLookupRequest(string SamAccountName);

/// <summary>Payload odpowiedzi na <c>ping</c>.</summary>
public sealed record PongResponse(DateTimeOffset ServerTimeUtc);

/// <summary>
/// Akcja na stacji (M4). <see cref="Confirm"/> musi mieć wartość <c>true</c> — serwer
/// odrzuca żądania bez jawnego potwierdzenia, niezależnie od tego, co pokazuje UI.
/// </summary>
public sealed record ComputerActionRequest(
    string ComputerName,
    ComputerAction Action,
    bool Confirm = false);

/// <summary>Reset hasła konta AD (M4). Wymaga <c>confirm = true</c>. Hasło nigdy nie trafia do audytu.</summary>
public sealed record ResetPasswordRequest(
    string SamAccountName,
    string NewPassword,
    bool Confirm = false);

/// <summary>Dodanie konta do grupy AD (M4). Wymaga <c>confirm = true</c>.</summary>
public sealed record AddToGroupRequest(
    string SamAccountName,
    string GroupName,
    bool Confirm = false);

/// <summary>Zapytanie o dziennik audytu (M3/M4). Wynik zawsze posortowany malejąco po czasie.</summary>
public sealed record AuditQueryRequest(
    string? Target = null,
    string? Capability = null,
    int Take = 100);
