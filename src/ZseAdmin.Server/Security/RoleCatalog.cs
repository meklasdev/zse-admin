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

    public static IReadOnlyList<string> CapabilitiesFor(string role) =>
        RoleCapabilities.TryGetValue(role, out var capabilities)
            ? capabilities
            : Array.Empty<string>();

    public static ClientIdentity CreateIdentity(string name, string role) =>
        new(name, role, CapabilitiesFor(role));
}
