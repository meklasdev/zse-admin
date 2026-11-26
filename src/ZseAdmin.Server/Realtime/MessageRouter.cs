using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Security;
using ZseAdmin.Server.Services;

namespace ZseAdmin.Server.Realtime;

/// <summary>
/// Jedno miejsce, w którym wiadomości klienta zamieniają się w odpowiedzi.
/// Każdy przypadek najpierw sprawdza uprawnienie, potem pyta źródło danych,
/// a na końcu dokłada metadane świeżości.
/// </summary>
public sealed class MessageRouter
{
    private const int DefaultLoginsTake = 100;
    private const int MaxLoginsTake = 500;

    private readonly IClassroomDirectory _classrooms;
    private readonly IComputerDirectory _computers;
    private readonly ILoginLog _logins;
    private readonly IAdDirectory _users;
    private readonly FreshnessService _freshness;
    private readonly ZseAdminOptions _options;
    private readonly IClock _clock;

    public MessageRouter(
        IClassroomDirectory classrooms,
        IComputerDirectory computers,
        ILoginLog logins,
        IAdDirectory users,
        FreshnessService freshness,
        ZseAdminOptions options,
        IClock clock)
    {
        _classrooms = classrooms;
        _computers = computers;
        _logins = logins;
        _users = users;
        _freshness = freshness;
        _options = options;
        _clock = clock;
    }

    public WelcomeResponse BuildWelcome(ClientIdentity identity, string sessionId) => new(
        ServerVersion: typeof(MessageRouter).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        ProtocolVersion: ProtocolJson.ProtocolVersion,
        SessionId: sessionId,
        UserName: identity.Name,
        Role: identity.Role,
        Capabilities: identity.Capabilities,
        ServerTimeUtc: _clock.UtcNow);

    public async Task<WsEnvelope> HandleAsync(
        WsEnvelope request,
        ClientIdentity identity,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Pole 'type' jest wymagane.");
        }

        switch (request.Type)
        {
            case MessageTypes.Ping:
                return WsEnvelope.Create(request.Type, request.RequestId, new PongResponse(_clock.UtcNow));

            case MessageTypes.ClassroomsList:
                return await HandleClassroomsAsync(request, identity, cancellationToken);

            case MessageTypes.ComputersList:
                return await HandleComputersAsync(request, identity, cancellationToken);

            case MessageTypes.LoginsQuery:
                return await HandleLoginsAsync(request, identity, cancellationToken);

            case MessageTypes.UsersGet:
                return await HandleUsersAsync(request, identity, cancellationToken);

            default:
                throw new ProtocolException(
                    ErrorCode.InvalidRequest,
                    $"Nieznany typ wiadomości: '{request.Type}'.");
        }
    }

    private async Task<WsEnvelope> HandleClassroomsAsync(
        WsEnvelope request,
        ClientIdentity identity,
        CancellationToken cancellationToken)
    {
        CapabilityGuard.Require(identity, Capability.ClassroomsRead);

        var result = await _classrooms.ListAsync(cancellationToken);
        var freshness = _freshness.Create(
            "Active Directory (OU)",
            result.Metadata,
            TimeSpan.FromSeconds(_options.Freshness.DirectorySeconds));

        return WsEnvelope.Create(request.Type, request.RequestId, new ClassroomsResponse(result.Items, freshness));
    }

    private async Task<WsEnvelope> HandleComputersAsync(
        WsEnvelope request,
        ClientIdentity identity,
        CancellationToken cancellationToken)
    {
        CapabilityGuard.Require(identity, Capability.ComputersRead);

        var query = request.ReadPayload<ComputerListRequest>() ?? new ComputerListRequest();
        var result = await _computers.ListAsync(query.ClassroomId, cancellationToken);

        IReadOnlyList<ComputerDto> items = query.IncludeOffline
            ? result.Items
            : result.Items.Where(c => c.Status == ComputerStatus.Online).ToList();

        var freshness = _freshness.Create(
            "Active Directory + sprawdzanie dostępności",
            result.Metadata,
            TimeSpan.FromSeconds(_options.Freshness.ComputersSeconds));

        return WsEnvelope.Create(request.Type, request.RequestId, new ComputersResponse(items, freshness));
    }

    private async Task<WsEnvelope> HandleLoginsAsync(
        WsEnvelope request,
        ClientIdentity identity,
        CancellationToken cancellationToken)
    {
        CapabilityGuard.Require(identity, Capability.LoginsRead);

        var query = request.ReadPayload<LoginQueryRequest>() ?? new LoginQueryRequest();
        query = query with
        {
            Take = Math.Clamp(query.Take <= 0 ? DefaultLoginsTake : query.Take, 1, MaxLoginsTake)
        };

        var result = await _logins.QueryAsync(query, cancellationToken);
        var freshness = _freshness.Create(
            "Dzienniki zabezpieczeń stacji",
            result.Metadata,
            TimeSpan.FromSeconds(_options.Freshness.LoginsSeconds));

        return WsEnvelope.Create(request.Type, request.RequestId, new LoginsResponse(result.Items, result.TotalMatched, freshness));
    }

    private async Task<WsEnvelope> HandleUsersAsync(
        WsEnvelope request,
        ClientIdentity identity,
        CancellationToken cancellationToken)
    {
        CapabilityGuard.Require(identity, Capability.UsersRead);

        var payload = request.ReadPayload<UserLookupRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.SamAccountName))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Pole 'samAccountName' jest wymagane.");
        }

        var result = await _users.FindUserAsync(payload.SamAccountName.Trim(), cancellationToken);
        var freshness = _freshness.Create(
            "Active Directory (użytkownicy)",
            result.Metadata,
            TimeSpan.FromSeconds(_options.Freshness.DirectorySeconds));

        if (result.User is null)
        {
            throw new ProtocolException(
                ErrorCode.NotFound,
                $"Nie znaleziono konta '{payload.SamAccountName}'.");
        }

        return WsEnvelope.Create(request.Type, request.RequestId, new UserResponse(result.User, freshness));
    }
}
