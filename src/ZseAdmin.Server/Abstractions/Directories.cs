using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;

namespace ZseAdmin.Server.Abstractions;

/// <summary>
/// Wynik zapytania wraz z metadanymi zaufania. Repozytoria nie zwracają "gołych" list,
/// bo UI musi umieć odróżnić brak danych od braku zdarzeń.
/// </summary>
public sealed record QueryMetadata(
    DateTimeOffset? ObservedAtUtc,
    DataCompleteness Completeness,
    string? Note)
{
    public static QueryMetadata Complete(DateTimeOffset observedAtUtc) =>
        new(observedAtUtc, DataCompleteness.Complete, null);

    public static QueryMetadata Unavailable(string note) =>
        new(null, DataCompleteness.Unavailable, note);
}

public sealed record ClassroomQueryResult(
    IReadOnlyList<ClassroomDto> Items,
    QueryMetadata Metadata);

public sealed record ComputerQueryResult(
    IReadOnlyList<ComputerDto> Items,
    QueryMetadata Metadata);

public sealed record LoginQueryResult(
    IReadOnlyList<LoginEventDto> Items,
    int TotalMatched,
    QueryMetadata Metadata);

public sealed record AdUserQueryResult(
    AdUserDto? User,
    QueryMetadata Metadata);

public interface IClassroomDirectory
{
    Task<ClassroomQueryResult> ListAsync(CancellationToken cancellationToken = default);
}

public interface IComputerDirectory
{
    Task<ComputerQueryResult> ListAsync(string? classroomId, CancellationToken cancellationToken = default);

    /// <summary>Odświeża stan komputerów; zwraca tylko te, których stan się zmienił.</summary>
    Task<IReadOnlyList<ComputerDto>> RefreshStatusesAsync(CancellationToken cancellationToken = default);
}

public interface ILoginLog
{
    Task<LoginQueryResult> QueryAsync(LoginQueryRequest query, CancellationToken cancellationToken = default);
}

public interface IAdDirectory
{
    Task<AdUserQueryResult> FindUserAsync(string samAccountName, CancellationToken cancellationToken = default);
}
