namespace ZseAdmin.Contracts.Protocol;

/// <summary>
/// Pierwsza wiadomość po połączeniu: tożsamość sesji, rola, uprawnienia i wersja serwera.
/// Klient nie może zakładać żadnego uprawnienia, którego tu nie ma.
/// </summary>
public sealed record WelcomeResponse(
    string ServerVersion,
    int ProtocolVersion,
    string SessionId,
    string UserName,
    string Role,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset ServerTimeUtc);

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

/// <summary>Odpowiedź błędu. Zawsze z tym samym <c>requestId</c>, o ile żądanie je miało.</summary>
public sealed record ErrorDto(
    ErrorCode Code,
    string Message,
    string? Detail = null);
