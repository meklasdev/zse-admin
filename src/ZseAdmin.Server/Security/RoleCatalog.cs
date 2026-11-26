namespace ZseAdmin.Server.Security;

/// <summary>Mapowanie roli na uprawnienia — jedyne źródło prawdy dla <see cref="ClientIdentity"/>.</summary>
public static class RoleCatalog
{
    private static readonly Dictionary<string, string[]> RoleCapabilities = new(StringComparer.OrdinalIgnoreCase)
    {
        [Roles.Administrator] = new[]
        {
            Capability.ClassroomsRead,
            Capability.ComputersRead,
            Capability.ComputersControl,
            Capability.LoginsRead,
            Capability.UsersRead,
            Capability.UsersWrite,
            Capability.AuditRead
        },
        [Roles.Teacher] = new[]
        {
            Capability.ClassroomsRead,
            Capability.ComputersRead,
            Capability.LoginsRead,
            Capability.UsersRead
        },
        [Roles.ReadOnly] = new[]
        {
            Capability.ClassroomsRead,
            Capability.ComputersRead,
            Capability.LoginsRead
        }
    };

    /// <summary>Kolejność rozstrzygania, gdy użytkownik należy do kilku grup.</summary>
    private static readonly string[] RolePriority =
    {
        Roles.Administrator, Roles.Teacher, Roles.ReadOnly
    };

    public static IReadOnlyList<string> CapabilitiesFor(string role) =>
        RoleCapabilities.TryGetValue(role, out var capabilities)
            ? capabilities
            : Array.Empty<string>();

    public static ClientIdentity CreateIdentity(string name, string role) =>
        new(name, role, CapabilitiesFor(role));

    public static ClientIdentity CreateIdentity(
        string name,
        string role,
        IEnumerable<string>? groups,
        string? remoteAddress,
        bool isSecureTransport) =>
        new(name, role, CapabilitiesFor(role), groups, remoteAddress, isSecureTransport);

    /// <summary>
    /// Wylicza rolę z grup AD. Brak którejkolwiek ze skonfigurowanych grup = <c>null</c>,
    /// czyli odmowa połączenia (kryterium M3: użytkownik spoza ZSE-Admin-Nauczyciele
    /// nie łączy się, chyba że należy do grupy administratorów lub ReadOnly).
    /// </summary>
    public static string? RoleFromGroups(
        IEnumerable<string> groups,
        IReadOnlyDictionary<string, string> roleGroups)
    {
        var normalized = new HashSet<string>(
            groups.Select(g => ShortName(g)).Where(g => g.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        foreach (var role in RolePriority)
        {
            foreach (var pair in roleGroups)
            {
                if (string.Equals(pair.Value, role, StringComparison.OrdinalIgnoreCase) &&
                    normalized.Contains(ShortName(pair.Key)))
                {
                    return role;
                }
            }
        }

        return null;
    }

    /// <summary><c>ZSE\ZSE-Admin-Nauczyciele</c> → <c>ZSE-Admin-Nauczyciele</c>.</summary>
    public static string ShortName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        var separator = trimmed.LastIndexOf('\\');
        return separator >= 0 && separator < trimmed.Length - 1
            ? trimmed[(separator + 1)..]
            : trimmed;
    }
}
