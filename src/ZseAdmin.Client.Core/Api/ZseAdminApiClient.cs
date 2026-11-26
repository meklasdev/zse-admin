using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Client.Core.Connection;

namespace ZseAdmin.Client.Core.Api;

/// <summary>
/// Typowane wywołania protokołu — reszta aplikacji nie zna JSON-a ani kopert.
/// Przekazuje też <c>welcome</c> (uprawnienia i zakres pracowni z M3), dzięki czemu
/// UI może ukryć akcje, których serwer i tak by odmówił.
/// </summary>
public sealed class ZseAdminApiClient
{
    private readonly WsClientConnection _connection;

    public ZseAdminApiClient(WsClientConnection connection)
    {
        _connection = connection;
        _connection.EventReceived += OnEventReceived;
    }

    public event EventHandler<ComputerStatusChangedEvent>? ComputerStatusChanged;

    /// <summary>Nowe zdarzenia logowań z bufora M2 (push).</summary>
    public event EventHandler<LoginEventsChangedEvent>? LoginEventsReceived;

    /// <summary>Pierwsza wiadomość serwera: rola, uprawnienia, zakres pracowni.</summary>
    public event EventHandler<WelcomeResponse>? WelcomeReceived;

    public Task<ClassroomsResponse> GetClassroomsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ClassroomsResponse>(MessageTypes.ClassroomsList, null, cancellationToken);

    public Task<ComputersResponse> GetComputersAsync(string? classroomId, CancellationToken cancellationToken = default) =>
        SendAsync<ComputersResponse>(MessageTypes.ComputersList, new ComputerListRequest(classroomId), cancellationToken);

    public Task<LoginsResponse> QueryLoginsAsync(LoginQueryRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<LoginsResponse>(MessageTypes.LoginsQuery, request, cancellationToken);

    public Task<UserResponse> GetUserAsync(string samAccountName, CancellationToken cancellationToken = default) =>
        SendAsync<UserResponse>(MessageTypes.UsersGet, new UserLookupRequest(samAccountName), cancellationToken);

    public Task<ActionPrepareResponse> PrepareActionAsync(
        ActionPrepareRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<ActionPrepareResponse>(MessageTypes.ActionPrepare, request, cancellationToken);

    public Task<ActionExecuteResponse> ExecuteActionAsync(
        ActionExecuteRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<ActionExecuteResponse>(MessageTypes.ActionExecute, request, cancellationToken);

    public Task<AuditQueryResponse> QueryAuditAsync(
        AuditQueryRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<AuditQueryResponse>(MessageTypes.AuditQuery, request, cancellationToken);

    private async Task<T> SendAsync<T>(string type, object? payload, CancellationToken cancellationToken)
    {
        var envelope = await _connection
            .SendRequestAsync(type, payload, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return envelope.ReadPayload<T>()
            ?? throw new InvalidOperationException($"Pusta odpowiedź na '{type}'.");
    }

    private void OnEventReceived(object? sender, WsEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case MessageTypes.ComputerStatusEvent:
                var status = envelope.ReadPayload<ComputerStatusChangedEvent>();
                if (status is not null)
                {
                    ComputerStatusChanged?.Invoke(this, status);
                }

                break;

            case MessageTypes.LoginEventsEvent:
                var logins = envelope.ReadPayload<LoginEventsChangedEvent>();
                if (logins is not null)
                {
                    LoginEventsReceived?.Invoke(this, logins);
                }

                break;

            case MessageTypes.Welcome:
                var welcome = envelope.ReadPayload<WelcomeResponse>();
                if (welcome is not null)
                {
                    WelcomeReceived?.Invoke(this, welcome);
                }

                break;
        }
    }
}
