namespace ZseAdmin.Contracts.Protocol;

/// <summary>Payloady żądań wysyłanych przez klienta.</summary>
public sealed record ComputerListRequest(
    string? ClassroomId = null,
    bool IncludeOffline = true);

/// <summary>
/// Zakres zapytania o historię logowań. Czasy są w UTC.
/// <c>ClassroomIds</c> jest ustawiane przez serwer dla nauczyciela (M3) — klient nie może
/// sam poszerzyć sobie zakresu; serwer i tak nadpisuje to pole.
/// </summary>
public sealed record LoginQueryRequest(
    string? ComputerName = null,
    string? UserSamAccountName = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Take = 100,
    string? ClassroomId = null,
    IReadOnlyList<string>? ClassroomIds = null);

public sealed record UserLookupRequest(string SamAccountName);

/// <summary>Payload odpowiedzi na <c>ping</c>.</summary>
public sealed record PongResponse(DateTimeOffset ServerTimeUtc);

/// <summary>
/// Krok 1 akcji M4: przygotowanie. Serwer sprawdza uprawnienie, pokazuje stan „przed”
/// i wydaje jednorazowy token ważny <c>ZseAdmin:Actions:ConfirmationSeconds</c>.
/// </summary>
public sealed record ActionPrepareRequest(
    AdminActionKind Action,
    string Target,
    string? GroupName = null,
    string? Reason = null);

/// <summary>
/// Krok 2 akcji M4: wykonanie. Bez <c>Confirm = true</c> i ważnego tokenu serwer odmawia.
/// </summary>
public sealed record ActionExecuteRequest(
    string ConfirmationToken,
    bool Confirm,
    string? Reason = null);

/// <summary>Zapytanie o wpisy audytu. <c>Target</c> = komputer lub konto, którego dotyczy zmiana.</summary>
public sealed record AuditQueryRequest(string? Target = null, int Take = 50);
