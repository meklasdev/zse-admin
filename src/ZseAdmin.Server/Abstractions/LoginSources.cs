using ZseAdmin.Contracts;

namespace ZseAdmin.Server.Abstractions;

/// <summary>
/// Status odczytu dziennika jednej stacji. Niedostępna stacja nie oznacza „brak logowań” —
/// trafia do metadanych kompletności odpowiedzi (kryterium M2).
/// </summary>
public sealed record StationLogStatus(string ComputerName, bool Available, string? Reason);

/// <summary>Jedna porcja danych ze źródła (WEF albo zdalny odczyt dzienników Security).</summary>
public sealed record LoginIngestBatch(
    IReadOnlyList<LoginEventDto> Events,
    IReadOnlyList<StationLogStatus> Stations);

/// <summary>
/// Port źródła zdarzeń 4624/4634/4625. Implementacje nie znają protokołu ani bufora —
/// dzięki temu retencję i kompletność można testować bez Windows i bez kontrolera domeny
/// (ADR-0006).
/// </summary>
public interface ILoginEventSource
{
    /// <summary>
    /// Zwraca zdarzenia od <paramref name="sinceUtc"/> (może zwrócić też starsze — bufor
    /// deduplikuje nakładające się odczyty) oraz status każdej stacji.
    /// </summary>
    Task<LoginIngestBatch> ReadSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);
}
