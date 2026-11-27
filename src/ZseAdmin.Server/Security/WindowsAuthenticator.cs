using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Security;

/// <summary>
/// M3 + D10 — uwierzytelnianie Windows/Kerberos. Host (IIS/HTTP.sys albo Kestrel z Negotiate)
/// ustawia <c>HttpContext.User</c>; tu mapujemy grupy AD na rolę.
/// <para>
/// Deny overrides (D10 = A): przynależność do grupy z <c>Security:DeniedGroups</c> (np. uczniowskiej)
/// odrzuca połączenie <b>przed</b> wyliczeniem roli — także wtedy, gdy konto omyłkowo trafiło do
/// <c>ZSE-Admin-Nauczyciele</c>. Odmowa = HTTP 401 przed upgrade'em WebSocket, bez <c>welcome</c>.
/// </para>
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

    public AuthenticationResult Authenticate(HttpRequest request)
    {
        var principal = request.HttpContext.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return AuthenticationResult.Reject(RejectionCodes.NotAuthenticated);
        }

        // W identity trzymamy obie formy nazwy (z prefiksem domeny i bez), żeby
        // konfiguracja i audyt mogły używać krótkiej nazwy grupy.
        var groups = _groups.GetGroups(principal)
            .SelectMany(g => new[] { g, RoleCatalog.ShortName(g) })
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // D10 = A: grupa wykluczająca ma pierwszeństwo nad jakąkolwiek rolą.
        if (RoleCatalog.FindDeniedGroup(groups, _options.DeniedGroups) is not null)
        {
            return AuthenticationResult.Reject(RejectionCodes.DeniedGroup);
        }

        var role = RoleCatalog.RoleFromGroups(groups, _options.RoleGroups);
        if (role is null)
        {
            // Kryterium M3: brak grupy ZSE-Admin-Nauczyciele (ani administratorskiej)
            // oznacza odmowę połączenia, a nie rolę z pustymi uprawnieniami.
            return AuthenticationResult.Reject(RejectionCodes.RoleGroupMissing);
        }

        var name = TeachingSlotOptions.Normalize(principal.Identity.Name ?? "unknown");
        var remoteAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();

        return AuthenticationResult.Success(
            RoleCatalog.CreateIdentity(name, role, groups, remoteAddress, _transport.IsSecure(request)));
    }
}
