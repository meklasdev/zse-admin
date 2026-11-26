using System.Security.Claims;
using System.Security.Principal;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Czyta grupy z <c>WindowsIdentity</c> (Negotiate/Kerberos) albo z oświadczeń
/// <c>GroupSid</c> dostarczonych przez IIS/HTTP.sys. SID-y są rozwiązywane na nazwy
/// kont; dodajemy też samą nazwę bez prefiksu domeny, żeby konfiguracja mogła używać
/// krótkiej formy <c>ZSE-Admin-Nauczyciele</c>.
/// </summary>
public sealed class WindowsGroupProvider : IWindowsGroupProvider
{
    public IReadOnlyList<string> GetGroups(ClaimsPrincipal principal)
    {
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (principal.Identity is WindowsIdentity windowsIdentity)
        {
            try
            {
                foreach (IdentityReference reference in windowsIdentity.Groups)
                {
                    AddSid(reference.Value, groups);
                }
            }
            catch (PlatformNotSupportedException)
            {
                // Poza Windows nie ma grup systemowych; oświadczenia poniżej wciąż działają.
            }
            catch (SystemException)
            {
                // Częściowo niedostępny kontekst domeny — nie zgadujemy roli.
            }
        }

        foreach (var claim in principal.FindAll(ClaimTypes.GroupSid))
        {
            AddSid(claim.Value, groups);
        }

        return groups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddSid(string? raw, HashSet<string> groups)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (raw.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var account = (NTAccount)new SecurityIdentifier(raw).Translate(typeof(NTAccount));
                groups.Add(account.Value);
                groups.Add(RoleCatalog.ShortName(account.Value));
                return;
            }
            catch (Exception ex) when (ex is SystemException or ArgumentException)
            {
                // SID spoza domeny — zapisujemy go w surowej postaci.
            }
        }

        groups.Add(raw.Trim());
        groups.Add(RoleCatalog.ShortName(raw));
    }
}
