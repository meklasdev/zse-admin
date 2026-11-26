using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Services;

/// <summary>
/// Zamienia metadane źródła na opis zaufania dla UI: czas obserwacji, starzenie i kompletność.
/// Bez znacznika czasu nie da się powiedzieć "online" — da się tylko "brak danych".
/// </summary>
public sealed class FreshnessService
{
    private readonly IClock _clock;

    public FreshnessService(IClock clock)
    {
        _clock = clock;
    }

    public DataFreshnessDto Create(string source, QueryMetadata metadata, TimeSpan maxAge)
    {
        if (metadata.ObservedAtUtc is not { } observedAt)
        {
            var completeness = metadata.Completeness == DataCompleteness.Complete
                ? DataCompleteness.Unavailable
                : metadata.Completeness;

            return new DataFreshnessDto(
                source,
                null,
                true,
                completeness,
                metadata.Note ?? "Brak danych ze źródła.");
        }

        var age = _clock.UtcNow - observedAt;
        var isStale = age > maxAge;

        return new DataFreshnessDto(
            source,
            observedAt,
            isStale,
            metadata.Completeness,
            metadata.Note ?? (isStale
                ? $"Dane starsze niż {maxAge.TotalSeconds:0} s (wiek {age.TotalSeconds:0} s)."
                : null));
    }
}
