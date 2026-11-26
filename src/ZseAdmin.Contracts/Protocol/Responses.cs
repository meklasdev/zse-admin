namespace ZseAdmin.Contracts.Protocol;

/// <summary>
/// Pierwsza wiadomość po połączeniu: tożsamość sesji, rola, uprawnienia i wersja serwera.
/// Klient nie może zakładać żadnego uprawnienia, którego tu nie ma.
/// <c>ClassroomScope</c> = pracownie, w których nauczyciel ma teraz zajęcia (M3);
/// <c>null</c> oznacza brak ograniczenia.
/// </summary>
public sealed record WelcomeResponse(
    string ServerVersion,
    int ProtocolVersion,
    string SessionId,
    string UserName,
    string Role,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset ServerTimeUtc,
    IReadOnlyList<string>? ClassroomScope = null);

public sealed record ClassroomsResponse(
    IReadOnlyList<ClassroomDto> Items,
    DataFreshnessDto Freshness);

public sealed record ComputersResponse(
    IReadOnlyList<ComputerDto> Items,
    DataFreshnessDto Freshness);

public sealed record LoginsResponse(
    IReadOnlyList<LoginEventDto> Items,
    int TotalMatched,
    DataFreshnessDto Freshness);

public sealed record UserResponse(
    AdUserDto User,
    DataFreshnessDto Freshness);

/// <summary>Odpowiedź <c>action.prepare</c>. Sama odpowiedź nic nie zmienia.</summary>
public sealed record ActionPrepareResponse(
    string ConfirmationToken,
    AdminActionKind Action,
    string Target,
    string Capability,
    string Description,
    string Before,
    IReadOnlyList<string> Warnings,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Odpowiedź <c>action.execute</c>. Błąd źródła nigdy nie jest tu raportowany jako sukces —
/// w takim przypadku przychodzi osobna wiadomość <c>error</c> z kodem <c>upstreamUnavailable</c>.
/// <c>TemporaryPassword</c> pojawia się wyłącznie po resecie hasła i tylko raz.
/// </summary>
public sealed record ActionExecuteResponse(
    bool Success,
    AdminActionKind Action,
    string Target,
    string Message,
    string Before,
    string After,
    string AuditId,
    string? TemporaryPassword,
    DataFreshnessDto Freshness);

/// <summary>Odpowiedź <c>audit.query</c> — wpisy widoczne w szczegółach obiektu.</summary>
public sealed record AuditQueryResponse(
    IReadOnlyList<AuditEntryDto> Items,
    int TotalMatched,
    DataFreshnessDto Freshness);

/// <summary>Odpowiedź błędu. Zawsze z tym samym <c>requestId</c>, o ile żądanie je miało.</summary>
public sealed record ErrorDto(
    ErrorCode Code,
    string Message,
    string? Detail = null);
