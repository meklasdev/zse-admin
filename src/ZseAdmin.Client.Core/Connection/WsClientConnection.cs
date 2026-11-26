using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using ZseAdmin.Contracts.Protocol;

namespace ZseAdmin.Client.Core.Connection;

/// <summary>
/// Klient WebSocket z automatycznym ponawianiem połączenia i korelacją odpowiedzi po <c>requestId</c>.
/// Ważne: utrata połączenia nie jest tym samym co offline komputera — to dwa różne stany w UI.
/// </summary>
public sealed class WsClientConnection : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly Uri _endpoint;
    private readonly string? _token;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WsEnvelope>> _pending = new(StringComparer.Ordinal);

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private ConnectionState _state = ConnectionState.Disconnected;

    public WsClientConnection(Uri endpoint, string? token = null)
    {
        _endpoint = endpoint;
        _token = token;
    }

    public ConnectionState State => _state;

    public event EventHandler<ConnectionState>? StateChanged;

    /// <summary>Wiadomości serwera bez <c>requestId</c> (zdarzenia push), np. zmiana stanu komputera.</summary>
    public event EventHandler<WsEnvelope>? EventReceived;

    /// <summary>Tekst diagnostyczny ostatniego błędu transportu — do paska statusu w UI.</summary>
    public string? LastTransportError { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loopTask is not null)
        {
            return Task.CompletedTask;
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _loopCts?.Cancel();

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // oczekiwane przy zatrzymaniu
            }
        }

        FailPending("Połączenie zostało zamknięte.");
    }

    public async Task<WsEnvelope> SendRequestAsync(
        string type,
        object? payload = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Brak połączenia z serwerem ZSE-Admin.");
        }

        var requestId = Guid.NewGuid().ToString("n");
        var envelope = new WsEnvelope { Type = type, RequestId = requestId };

        if (payload is not null)
        {
            envelope.Payload = JsonSerializer.SerializeToElement(payload, payload.GetType(), ProtocolJson.Options);
        }

        var completion = new TaskCompletionSource<WsEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = completion;

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, ProtocolJson.Options);

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket
                    .SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            var effectiveTimeout = timeout ?? RequestTimeout;
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var timeoutRegistration = timeoutCts.Token.Register(() =>
                completion.TrySetException(new TimeoutException(
                    $"Brak odpowiedzi na '{type}' w {effectiveTimeout.TotalSeconds:0} s.")));
            using var cancelRegistration = cancellationToken.Register(() =>
                completion.TrySetCanceled(cancellationToken));

            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _socket?.Dispose();
        _sendLock.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetState(attempt == 0 ? ConnectionState.Connecting : ConnectionState.Reconnecting);

                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                // M3: bez tokenu używamy zintegrowanego uwierzytelniania Windows
                // (Negotiate/Kerberos) — dotyczy wss:// w trybie produkcyjnym.
                if (string.IsNullOrWhiteSpace(_token))
                {
                    socket.Options.UseDefaultCredentials = true;
                }

                await socket.ConnectAsync(BuildUri(), cancellationToken).ConfigureAwait(false);

                _socket = socket;
                LastTransportError = null;
                attempt = 0;
                SetState(ConnectionState.Connected);

                await ReceiveLoopAsync(socket, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LastTransportError = ex.Message;
            }
            finally
            {
                _socket = null;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            FailPending("Połączenie z serwerem zostało utracone.");
            SetState(ConnectionState.Reconnecting);

            attempt = Math.Min(attempt + 1, 6);
            var delay = TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, attempt)));

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetState(ConnectionState.Disconnected);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1024];
        using var message = new MemoryStream();

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;

            do
            {
                result = await socket
                    .ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                    .ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                message.Write(buffer, 0, result.Count);
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
            catch (JsonException)
            {
                continue;
            }

            if (envelope is null)
            {
                continue;
            }

            Dispatch(envelope);
        }
    }

    private void Dispatch(WsEnvelope envelope)
    {
        if (envelope.RequestId is { } requestId && _pending.TryRemove(requestId, out var completion))
        {
            if (envelope.Type == MessageTypes.Error)
            {
                var error = envelope.ReadPayload<ErrorDto>();
                completion.TrySetException(new ServerErrorException(
                    error?.Code ?? ErrorCode.Internal,
                    error?.Message ?? "Serwer zwrócił błąd.",
                    error?.Detail));

                return;
            }

            completion.TrySetResult(envelope);
            return;
        }

        if (envelope.Type == MessageTypes.Error)
        {
            return;
        }

        EventReceived?.Invoke(this, envelope);
    }

    private Uri BuildUri()
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            return _endpoint;
        }

        var builder = new UriBuilder(_endpoint);
        var existing = builder.Query.TrimStart('?');
        var tokenQuery = $"access_token={Uri.EscapeDataString(_token)}";
        builder.Query = string.IsNullOrEmpty(existing) ? tokenQuery : $"{existing}&{tokenQuery}";
        return builder.Uri;
    }

    private void FailPending(string reason)
    {
        foreach (var key in _pending.Keys.ToList())
        {
            if (_pending.TryRemove(key, out var completion))
            {
                completion.TrySetException(new InvalidOperationException(reason));
            }
        }
    }

    private void SetState(ConnectionState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, state);
    }
}
