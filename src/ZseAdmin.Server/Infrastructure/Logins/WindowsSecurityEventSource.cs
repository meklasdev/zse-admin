using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>
/// Zdalny odczyt dziennika zabezpieczeń stacji (4624/4634/4625) przez
/// <c>System.Diagnostics.Eventing.Reader</c> — jedyny sposób, żeby zapytać zdalny
/// dziennik bez agentow. Wymaga: konto usługi w grupie „Czytelnicy dzienników zdarzeń”
/// na stacjach oraz dostępu sieciowego (RPC/SMB).
///
/// Implementacja właściwa żyje w <see cref="WindowsSecurityEventReader"/>, żeby
/// poniższa klasa mogła zostać zbudowana/załadowana także poza Windows
/// (tam zwraca jawne „niedostępne”, a nie wyjątek typu TypeLoadException).
/// </summary>
public sealed class WindowsSecurityEventSource : ISecurityEventSource
{
    public Task<IReadOnlyList<StationSecurityLogResult>> QueryStationsAsync(
        IReadOnlyList<SecurityLogStationQuery> queries,
        TimeSpan budget,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            IReadOnlyList<StationSecurityLogResult> unavailable = queries
                .Select(q => new StationSecurityLogResult(
                    q.ComputerName,
                    false,
                    "Odczyt dzienników zabezpieczeń jest możliwy wyłącznie na Windows (konto usługi w grupie „Czytelnicy dzienników zdarzeń” na stacjach).",
                    Array.Empty<SecurityLogEvent>()))
                .ToList();

            return Task.FromResult(unavailable);
        }

        return WindowsSecurityEventReader.ReadAsync(queries, budget, cancellationToken);
    }
}
