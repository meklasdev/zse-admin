using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Rozwiązuje przynależność użytkownika do grup AD (nazwy grup, z zagnieżdżeniem) —
/// podstawa mapowania grup → rola w trybie ActiveDirectory. Nieosiągalne AD rzuca
/// <see cref="SourceUnavailableException"/> (serwer odpowie 503, a nie 401).
/// </summary>
public interface IAdGroupMembershipResolver
{
    Task<IReadOnlyList<string>> GetGroupNamesAsync(
        string samAccountName,
        CancellationToken cancellationToken = default);
}
