using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Ldap;

/// <summary>
/// Konta z AD na potrzeby <c>users.get</c>. Nieznane konto to <c>null</c> (router zwróci
/// <c>notFound</c>), a nieosiągalne AD to <see cref="SourceUnavailableException"/>
/// (<c>upstreamUnavailable</c>) — te dwa przypadki nie mogą się mylić.
/// </summary>
public sealed class AdDirectory : IAdDirectory
{
    private readonly IAdInventoryReader _inventory;
    private readonly IClock _clock;

    public AdDirectory(IAdInventoryReader inventory, IClock clock)
    {
        _inventory = inventory;
        _clock = clock;
    }

    public async Task<AdUserQueryResult> FindUserAsync(
        string samAccountName,
        CancellationToken cancellationToken = default)
    {
        var user = await _inventory.FindUserAsync(samAccountName, cancellationToken).ConfigureAwait(false);

        return new AdUserQueryResult(user, QueryMetadata.Complete(_clock.UtcNow));
    }
}
