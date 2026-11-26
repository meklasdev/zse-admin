using System.Collections.Concurrent;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Security;

namespace ZseAdmin.Server.Realtime;

/// <summary>Rejestr aktywnych sesji WebSocket — potrzebny do rozgłaszania zdarzeń.</summary>
public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, WsConnection> _connections = new(StringComparer.Ordinal);

    public int Count => _connections.Count;

    public void Add(WsConnection connection) => _connections[connection.Id] = connection;

    public void Remove(WsConnection connection) => _connections.TryRemove(connection.Id, out _);

    /// <summary>Wysyła kopertę do wszystkich sesji mających dane uprawnienie; błędy pojedynczej sesji są ignorowane.</summary>
    public async Task BroadcastAsync(WsEnvelope envelope, string capability, CancellationToken cancellationToken)
    {
        foreach (var connection in _connections.Values)
        {
            if (!connection.Identity.Has(capability))
            {
                continue;
            }

            try
            {
                await connection.SendAsync(envelope, cancellationToken);
            }
            catch (Exception)
            {
                // Sesja właśnie się zamyka — rejestr posprząta ją w finally pętli odbioru.
            }
        }
    }
}
