using System.Collections.ObjectModel;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Tożsamość jednej sesji WebSocket wraz z policzonym zestawem uprawnień.
/// <c>Groups</c> i <c>RemoteAddress</c> (M3) są potrzebne audytowi i rozliczalności.
/// </summary>
public sealed class ClientIdentity
{
    private readonly HashSet<string> _capabilities;

    public ClientIdentity(
        string name,
        string role,
        IEnumerable<string> capabilities,
        IEnumerable<string>? groups = null,
        string? remoteAddress = null,
        bool isSecureTransport = false)
    {
        Name = name;
        Role = role;
        _capabilities = new HashSet<string>(capabilities, StringComparer.Ordinal);
        Capabilities = new ReadOnlyCollection<string>(
            _capabilities.OrderBy(c => c, StringComparer.Ordinal).ToList());
        Groups = new ReadOnlyCollection<string>(
            (groups ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        RemoteAddress = remoteAddress;
        IsSecureTransport = isSecureTransport;
    }

    public string Name { get; }

    public string Role { get; }

    public IReadOnlyList<string> Capabilities { get; }

    /// <summary>Grupy AD (tryb Windows); w trybie Token pusta lista.</summary>
    public IReadOnlyList<string> Groups { get; }

    public string? RemoteAddress { get; }

    /// <summary>Czy sesja przyszła po HTTPS/<c>wss</c> (wymagane dla resetu hasła).</summary>
    public bool IsSecureTransport { get; }

    public bool Has(string capability) => _capabilities.Contains(capability);
}
