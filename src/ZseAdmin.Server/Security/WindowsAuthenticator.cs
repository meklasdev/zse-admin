using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Security;

/// <summary>
/// M3 — uwierzytelnianie Windows/Kerberos. Host (IIS/HTTP.sys albo Kestrel z Negotiate)
/// ustawia <c>HttpContext.User</c>; tu tylko mapujemy grupy AD na rolę. Użytkownik bez
/// grupy z <c>Security:RoleGroups</c> nie dostaje sesji (401 przed upgrade'em).
/// </summary>
public sealed class WindowsAuthenticator : ITokenAuthenticator
{
    private readonly SecurityOptions _options;
    private readonly IWindowsGroupProvider _groups;
    private readonly ITransportSecurityGuard _transport;

    public WindowsAuthenticator(
        SecurityOptions options,
        IWindowsGroupProvider groups,
        ITransportSecurityGuard transport)
    {
        _options = options;
        _groups = groups;
        _transport = transport;
    }

    public ClientIdentity? Authenticate(HttpRequest request)
    {
        var principal = request.HttpContext.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // W identity trzymamy obie formy nazwy (z prefiksem domeny i bez), żeby
        // konfiguracja i audyt mogły używać krótkiej nazwy grupy.
        var groups = _groups.GetGroups(principal)
            .SelectMany(g => new[] { g, RoleCatalog.ShortName(g) })
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var role = RoleCatalog.RoleFromGroups(groups, _options.RoleGroups);
        if (role is null)
        {
            // Kryterium M3: brak grupy ZSE-Admin-Nauczyciele (ani administratorskiej)
            // oznacza odmowę połączenia, a nie rolę z pustymi uprawnieniami.
            return null;
        }

        var name = TeachingSlotOptions.Normalize(principal.Identity.Name ?? "unknown");
        var remoteAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();

        return RoleCatalog.CreateIdentity(
            name, role, groups, remoteAddress, _transport.IsSecure(request));
    }
}
