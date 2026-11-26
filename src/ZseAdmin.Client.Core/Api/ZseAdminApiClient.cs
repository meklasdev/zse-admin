using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Client.Core.Connection;

namespace ZseAdmin.Client.Core.Api;

/// <summary>Typowane wywołania protokołu — reszta aplikacji nie zna JSON-a ani kopert.</summary>
public sealed class ZseAdminApiClient
{
    private readonly WsClientConnection _connection;

    public ZseAdminApiClient(WsClientConnection connection)
    {
        _connection = connection;
        _connection.EventReceived += OnEventReceived;
    }

    public event EventHandler<ComputerStatusChangedEvent>? ComputerStatusChanged;

    /// <summary>Powitanie z uprawnieniami sesji (pola <c>welcome</c>); ponawiane przy każdym reconnect.</summary>
    public event EventHandler<WelcomeResponse>? WelcomeReceived;

    /// <summary>Ostatnio odebrane powitanie; <c>null</c> przed pierwszym połączeniem.</summary>
    public WelcomeResponse? Welcome { get; private set; }

    public Task<ClassroomsResponse> GetClassroomsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ClassroomsResponse>(MessageTypes.ClassroomsList, null, cancellationToken);

    public Task<ComputersResponse> GetComputersAsync(string? classroomId, CancellationToken cancellationToken = default) =>
        SendAsync<ComputersResponse>(MessageTypes.ComputersList, new ComputerListRequest(classroomId), cancellationToken);

    public Task<LoginsResponse> QueryLoginsAsync(LoginQueryRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<LoginsResponse>(MessageTypes.LoginsQuery, request, cancellationToken);

    public Task<UserResponse> GetUserAsync(string samAccountName, CancellationToken cancellationToken = default) =>
        SendAsync<UserResponse>(MessageTypes.UsersGet, new UserLookupRequest(samAccountName), cancellationToken);

    public Task<ComputerActionResponse> RunComputerActionAsync(
        string computerName,
        ComputerAction action,
        CancellationToken cancellationToken = default) =>
        SendAsync<ComputerActionResponse>(
            MessageTypes.ComputersAction,
            new ComputerActionRequest(computerName, action, Confirm: true),
            cancellationToken);

    public Task<ResetPasswordResponse> ResetPasswordAsync(
        string samAccountName,
        string newPassword,
        CancellationToken cancellationToken = default) =>
        SendAsync<ResetPasswordResponse>(
            MessageTypes.UsersResetPassword,
            new ResetPasswordRequest(samAccountName, newPassword, Confirm: true),
            cancellationToken);

    public Task<AddToGroupResponse> AddToGroupAsync(
        string samAccountName,
        string groupName,
        CancellationToken cancellationToken = default) =>
        SendAsync<AddToGroupResponse>(
            MessageTypes.UsersAddToGroup,
            new AddToGroupRequest(samAccountName, groupName, Confirm: true),
            cancellationToken);

    public Task<AuditResponse> QueryAuditAsync(
        string? target = null,
        string? capability = null,
        int take = 50,
        CancellationToken cancellationToken = default) =>
        SendAsync<AuditResponse>(
            MessageTypes.AuditQuery,
            new AuditQueryRequest(target, capability, take),
            cancellationToken);

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
        if (envelope.Type == MessageTypes.Welcome)
        {
            var welcome = envelope.ReadPayload<WelcomeResponse>();
            if (welcome is not null)
            {
                Welcome = welcome;
                WelcomeReceived?.Invoke(this, welcome);
            }

            return;
        }

        if (envelope.Type != MessageTypes.ComputerStatusEvent)
        {
            return;
        }

        var payload = envelope.ReadPayload<ComputerStatusChangedEvent>();
        if (payload is not null)
        {
            ComputerStatusChanged?.Invoke(this, payload);
        }
    }
}
