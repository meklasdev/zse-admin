using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Security;

namespace ZseAdmin.Server.Realtime;

/// <summary>
/// Jedna sesja WebSocket: powitanie, pętla odbioru, dyspozycja wiadomości i wysyłka.
/// Wysyłka jest serializowana semaforem, bo rozgłaszanie zdarzeń idzie z innego wątku
/// niż odpowiedzi na żądania.
/// </summary>
public sealed class WsConnection
{
    private const int MaxMessageBytes = 1024 * 1024;
    private const int ReceiveBufferBytes = 8 * 1024;

    private readonly WebSocket _socket;
    private readonly MessageRouter _router;
    private readonly ConnectionRegistry _registry;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public WsConnection(
        WebSocket socket,
        ClientIdentity identity,
        MessageRouter router,
        ConnectionRegistry registry,
        ILogger logger)
    {
        _socket = socket;
        Identity = identity;
        _router = router;
        _registry = registry;
        _logger = logger;
    }

    public string Id { get; } = Guid.NewGuid().ToString("n");

    public ClientIdentity Identity { get; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _registry.Add(this);
        _logger.LogInformation(
            "Sesja {SessionId} otwarta dla {User} ({Role}).",
            Id,
            Identity.Name,
            Identity.Role);

        try
        {
            var welcome = WsEnvelope.Create(MessageTypes.Welcome, null, _router.BuildWelcome(Identity, Id));
            await SendAsync(welcome, cancellationToken);

            await ReceiveLoopAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Klient rozłączył się lub serwer się zatrzymuje.
        }
        catch (WebSocketException ex)
        {
            _logger.LogInformation(ex, "Sesja {SessionId} przerwana przez błąd transportu.", Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nieoczekiwany błąd sesji {SessionId}.", Id);
        }
        finally
        {
            _registry.Remove(this);
            _logger.LogInformation("Sesja {SessionId} zamknięta.", Id);
        }
    }

    public async Task SendAsync(WsEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
        {
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, ProtocolJson.Options);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_socket.State != WebSocketState.Open)
            {
                return;
            }

            await _socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferBytes];
        using var message = new MemoryStream();

        while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;

            do
            {
                result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseAsync("Kanał zamknięty przez klienta.", cancellationToken);
                    return;
                }

                message.Write(buffer, 0, result.Count);

                if (message.Length > MaxMessageBytes)
                {
                    _logger.LogWarning("Sesja {SessionId}: wiadomość przekroczyła {Limit} B.", Id, MaxMessageBytes);
                    await CloseAsync("Wiadomość zbyt duża.", cancellationToken);
                    return;
                }
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            WsEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<WsEnvelope>(message.ToArray(), ProtocolJson.Options);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Sesja {SessionId}: nieczytelny JSON.", Id);
                await SendAsync(
                    WsEnvelope.Create(MessageTypes.Error, null, new ErrorDto(ErrorCode.InvalidRequest, "Nieczytelny JSON.")),
                    cancellationToken);
                continue;
            }

            if (envelope is null)
            {
                continue;
            }

            await DispatchAsync(envelope, cancellationToken);
        }
    }

    private async Task DispatchAsync(WsEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _router.HandleAsync(envelope, Identity, cancellationToken);
            await SendAsync(response, cancellationToken);
        }
        catch (ProtocolException ex)
        {
            _logger.LogInformation(
                "Sesja {SessionId}: {Type} odrzucone ({Code}): {Message}",
                Id,
                envelope.Type,
                ex.Code,
                ex.Message);

            await SendAsync(
                WsEnvelope.Create(MessageTypes.Error, envelope.RequestId, new ErrorDto(ex.Code, ex.Message)),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sesja {SessionId}: błąd obsługi {Type}.", Id, envelope.Type);
            await SendAsync(
                WsEnvelope.Create(
                    MessageTypes.Error,
                    envelope.RequestId,
                    new ErrorDto(ErrorCode.Internal, "Wewnętrzny błąd serwera.")),
                cancellationToken);
        }
    }

    private async Task CloseAsync(string reason, CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken);
            }
            catch (WebSocketException)
            {
                // Połączenie już nie istnieje.
            }
        }
    }
}
