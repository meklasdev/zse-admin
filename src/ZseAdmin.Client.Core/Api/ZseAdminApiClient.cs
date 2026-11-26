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

    public Task<ClassroomsResponse> GetClassroomsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ClassroomsResponse>(MessageTypes.ClassroomsList, null, cancellationToken);

    public Task<ComputersResponse> GetComputersAsync(string? classroomId, CancellationToken cancellationToken = default) =>
        SendAsync<ComputersResponse>(MessageTypes.ComputersList, new ComputerListRequest(classroomId), cancellationToken);

    public Task<LoginsResponse> QueryLoginsAsync(LoginQueryRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<LoginsResponse>(MessageTypes.LoginsQuery, request, cancellationToken);

    public Task<UserResponse> GetUserAsync(string samAccountName, CancellationToken cancellationToken = default) =>
        SendAsync<UserResponse>(MessageTypes.UsersGet, new UserLookupRequest(samAccountName), cancellationToken);

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
