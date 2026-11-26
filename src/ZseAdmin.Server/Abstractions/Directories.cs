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

    public static QueryMetadata Partial(DateTimeOffset observedAtUtc, string note) =>
        new(observedAtUtc, DataCompleteness.Partial, note);

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
    /// <summary>Początek retencji źródła (UTC); <c>null</c> = źródło nie deklaruje zakresu.</summary>
    DateTimeOffset? RetentionStartUtc { get; }

    /// <summary>
    /// <paramref name="allowedClassroomIds"/> (M3) ogranicza widoczność do pracowni —
    /// nauczyciel widzi wyłącznie zdarzenia ze stacji w pracowniach mu przypisanych.
    /// <c>null</c> = bez ograniczenia (Administrator, ReadOnly).
    /// </summary>
    Task<LoginQueryResult> QueryAsync(
        LoginQueryRequest query,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? allowedClassroomIds = null);
}

public interface IAdDirectory
{
    /// <summary>
    /// Zwraca konto albo <c>null</c> (<c>notFound</c>). Gdy źródło jest nieosiągalne,
    /// implementacja rzuca <see cref="SourceUnavailableException"/> — puste konto
    /// nie może udawać „nie ma takiego użytkownika”.
    /// </summary>
    Task<AdUserQueryResult> FindUserAsync(string samAccountName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Źródło danych nie jest osiągalne (AD, dziennik). Router zamienia to na błąd
/// <c>upstreamUnavailable</c> z komunikatem, a nie na pustą listę.
/// </summary>
public sealed class SourceUnavailableException : Exception
{
    public SourceUnavailableException(string sourceName, string message, Exception? inner = null)
        : base(message, inner)
    {
        SourceName = sourceName;
    }

    /// <summary>Nazwa źródła danych (np. „Active Directory”); nie mylić z <see cref="Exception.Source"/>.</summary>
    public string SourceName { get; }
}
