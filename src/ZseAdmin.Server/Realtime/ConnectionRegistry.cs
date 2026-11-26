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
    public Task BroadcastAsync(
        WsEnvelope envelope,
        string capability,
        CancellationToken cancellationToken) =>
        BroadcastAsync(
            identity => identity.Has(capability) ? envelope : null,
            cancellationToken);

    /// <summary>
    /// Wariant z filtrem per sesja (M3): kolektor logowań buduje kopertę tylko z tymi
    /// zdarzeniami, które dana tożsamość może zobaczyć. <c>null</c> = nic nie wysyłaj.
    /// </summary>
    public async Task BroadcastAsync(
        Func<ClientIdentity, WsEnvelope?> envelopeFactory,
        CancellationToken cancellationToken)
    {
        foreach (var connection in _connections.Values)
        {
            WsEnvelope? envelope;
            try
            {
                envelope = envelopeFactory(connection.Identity);
            }
            catch (Exception)
            {
                // Błąd polityki nie może przerwać rozgłaszania do pozostałych sesji.
                continue;
            }

            if (envelope is null)
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
