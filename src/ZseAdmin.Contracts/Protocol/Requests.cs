namespace ZseAdmin.Contracts.Protocol;

/// <summary>Payloady żądań wysyłanych przez klienta.</summary>
public sealed record ComputerListRequest(
    string? ClassroomId = null,
    bool IncludeOffline = true);

/// <summary>Zakres zapytania o historię logowań. Czasy są w UTC.</summary>
public sealed record LoginQueryRequest(
    string? ComputerName = null,
    string? UserSamAccountName = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Take = 100);

public sealed record UserLookupRequest(string SamAccountName);

/// <summary>Payload odpowiedzi na <c>ping</c>.</summary>
public sealed record PongResponse(DateTimeOffset ServerTimeUtc);
