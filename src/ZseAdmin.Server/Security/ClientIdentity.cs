using System.Collections.ObjectModel;

namespace ZseAdmin.Server.Security;

/// <summary>Tożsamość jednej sesji WebSocket wraz z policzonym zestawem uprawnień.</summary>
public sealed class ClientIdentity
{
    private readonly HashSet<string> _capabilities;

    public ClientIdentity(string name, string role, IEnumerable<string> capabilities)
    {
        Name = name;
        Role = role;
        _capabilities = new HashSet<string>(capabilities, StringComparer.Ordinal);
        Capabilities = new ReadOnlyCollection<string>(
            _capabilities.OrderBy(c => c, StringComparer.Ordinal).ToList());
    }

    public string Name { get; }

    public string Role { get; }

    public IReadOnlyList<string> Capabilities { get; }

    public bool Has(string capability) => _capabilities.Contains(capability);
}
