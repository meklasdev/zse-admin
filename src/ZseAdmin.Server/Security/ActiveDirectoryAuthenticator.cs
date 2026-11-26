using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Uwierzytelnianie Windows/Kerberos (Negotiate) + mapowanie grup AD na role (M3):
/// <list type="bullet">
/// <item><c>ZSE-Admin-Administratorzy</c> → Administrator (konfigurowalne),</item>
/// <item><c>ZSE-Admin-Nauczyciele</c> → Nauczyciel,</item>
/// <item><c>ZSE-Admin-Podglad</c> → ReadOnly (konfigurowalne, puste = wyłączone).</item>
/// </list>
/// Użytkownik spoza wszystkich trzech grup <b>nie łączy się</b> (401). Tożsamość pochodzi
/// z Negotiate na poziomie żądania (IIS lub <c>Microsoft.AspNetCore.Authentication.Negotiate</c>
/// na Kestrel); role z grup odczytujemy z AD przy każdym połączeniu — świeżo po
/// zmianie przynależności, bez restartu.
/// </summary>
public sealed class ActiveDirectoryAuthenticator : IRequestAuthenticator
{
    private readonly IAdGroupMembershipResolver _groups;
    private readonly SecurityOptions _options;
    private readonly ILogger<ActiveDirectoryAuthenticator> _logger;

    public ActiveDirectoryAuthenticator(
        IAdGroupMembershipResolver groups,
        SecurityOptions options,
        ILogger<ActiveDirectoryAuthenticator> logger)
    {
        _groups = groups;
        _options = options;
        _logger = logger;
    }

    public async Task<ClientIdentity?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var principal = context.User;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            _logger.LogInformation(
                "Odmowa połączenia — brak uwierzytelnionego użytkownika Windows (Negotiate). " +
                "Upewnij się, że serwer działa za IIS z uwierzytelnianiem Windows albo ma zarejestrowany handler Negotiate.");
            return null;
        }

        var sam = NormalizeSamAccountName(principal.Identity.Name);
        if (sam is null)
        {
            _logger.LogInformation("Odmowa połączenia — nie da się zinterpretować nazwy tożsamości '{Name}'.", principal.Identity.Name);
            return null;
        }

        // Nieosiągalne AD nie może przejść w „puste grupy” (a więc w 401 albo, gorzej, w puste uprawnienia).
        var groups = await _groups.GetGroupNamesAsync(sam, cancellationToken).ConfigureAwait(false);
        var mapping = _options.GroupMapping;

        if (ContainsGroup(groups, mapping.AdministratorGroup))
        {
            return RoleCatalog.CreateIdentity(sam, Roles.Administrator);
        }

        if (ContainsGroup(groups, mapping.TeacherGroup))
        {
            return RoleCatalog.CreateIdentity(sam, Roles.Teacher);
        }

        if (ContainsGroup(groups, mapping.ReadOnlyGroup))
        {
            return RoleCatalog.CreateIdentity(sam, Roles.ReadOnly);
        }

        _logger.LogWarning(
            "Użytkownik {User} nie należy do żadnej skonfigurowanej grupy ZSE-Admin-* — odmowa połączenia.",
            sam);
        return null;
    }

    /// <summary><c>ZSE\\kowalski</c> → <c>kowalski</c>; <c>kowalski@zse.local</c> → <c>kowalski</c>.</summary>
    public static string? NormalizeSamAccountName(string? identityName)
    {
        if (string.IsNullOrWhiteSpace(identityName))
        {
            return null;
        }

        var name = identityName.Trim();
        var backslash = name.LastIndexOf('\\');
        if (backslash >= 0 && backslash < name.Length - 1)
        {
            name = name[(backslash + 1)..];
        }
        else
        {
            var at = name.IndexOf('@');
            if (at > 0)
            {
                name = name[..at];
            }
        }

        name = name.Trim();
        return name.Length == 0 ? null : name;
    }

    private static bool ContainsGroup(IReadOnlyList<string> groups, string? wanted) =>
        !string.IsNullOrWhiteSpace(wanted)
        && groups.Any(group => string.Equals(group, wanted.Trim(), StringComparison.OrdinalIgnoreCase));
}
