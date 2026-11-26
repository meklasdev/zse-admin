using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Security;
using ZseAdmin.Server.Services;

namespace ZseAdmin.Server.Realtime;

/// <summary>
/// Jedno miejsce, w którym wiadomości klienta zamieniają się w odpowiedzi.
/// Każdy przypadek najpierw sprawdza uprawnienie (M0), potem zakres danych (M3),
/// pyta źródło i na końcu dokłada metadane świeżości. Akcje (M4) idą przez
/// <see cref="AdminActionService"/> z potwierdzeniem i audytem.
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
    private readonly ILoginAccessPolicy _loginAccess;
    private readonly IAuditLog _audit;
    private readonly AdminActionService _actions;

    public MessageRouter(
        IClassroomDirectory classrooms,
        IComputerDirectory computers,
        ILoginLog logins,
        IAdDirectory users,
        FreshnessService freshness,
        ZseAdminOptions options,
        IClock clock,
        ILoginAccessPolicy loginAccess,
        IAuditLog audit,
        AdminActionService actions)
    {
        _classrooms = classrooms;
        _computers = computers;
        _logins = logins;
        _users = users;
        _freshness = freshness;
        _options = options;
        _clock = clock;
        _loginAccess = loginAccess;
        _audit = audit;
        _actions = actions;
    }

    public WelcomeResponse BuildWelcome(ClientIdentity identity, string sessionId) => new(
        ServerVersion: typeof(MessageRouter).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        ProtocolVersion: ProtocolJson.ProtocolVersion,
        SessionId: sessionId,
        UserName: identity.Name,
        Role: identity.Role,
        Capabilities: identity.Capabilities,
        ServerTimeUtc: _clock.UtcNow,
        ClassroomScope: _loginAccess.ClassroomScopeFor(identity));

    public async Task<WsEnvelope> HandleAsync(
        WsEnvelope request,
        ClientIdentity identity,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await HandleCoreAsync(request, identity, cancellationToken).ConfigureAwait(false);
        }
        catch (SourceUnavailableException ex)
        {
            // Źródło nieosiągalne to nie „pusto”. Klient dostaje błąd z nazwą źródła,
            // więc może zaproponować ponowienie — patrz docs/model-danych.md §4.
            throw new ProtocolException(
                ErrorCode.UpstreamUnavailable,
                $"Źródło danych niedostępne ({ex.SourceName}): {ex.Message}",
                ex.Message);
        }
    }

    private async Task<WsEnvelope> HandleCoreAsync(
        WsEnvelope request,
        ClientIdentity identity,
        CancellationToken cancellationToken)
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

            case MessageTypes.AuditQuery:
                return await HandleAuditAsync(request, identity, cancellationToken);

            case MessageTypes.ActionPrepare:
                return await HandleActionPrepareAsync(request, identity, cancellationToken);

            case MessageTypes.ActionExecute:
                return await HandleActionExecuteAsync(request, identity, cancellationToken);

            case MessageTypes.Welcome:
            case MessageTypes.ComputerStatusEvent:
            case MessageTypes.LoginEventsEvent:
                // Powitanie i zdarzenia wysyła wyłącznie serwer.
                throw new ProtocolException(
                    ErrorCode.InvalidRequest,
                    $"Typ '{request.Type}' jest zarezerwowany dla serwera.");

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

        // M3: serwer nadpisuje zakres pracowni, nawet jeśli klient go podał.
        query = await _loginAccess.ScopeAsync(identity, query, cancellationToken).ConfigureAwait(false);

        var result = await _logins.QueryAsync(query, cancellationToken);
        var freshness = _freshness.Create(
            "Dzienniki zabezpieczeń stacji",
            result.Metadata,
            TimeSpan.FromSeconds(_options.Freshness.LoginsSeconds));

        // Pytanie o zakres poza retencją nie może wyglądać jak „nikt się nie logował”.
        // Bufor M2 też to sygnalizuje — nie powielamy komunikatu.
        if (query.FromUtc is { } from &&
            _logins.RetentionStartUtc is { } retentionStart &&
            from < retentionStart &&
            !(freshness.Note?.Contains("retencj", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            var rangeNote =
                $"Żądany zakres zaczyna się przed retencją historii (od {retentionStart:yyyy-MM-dd HH:mm} UTC).";

            freshness = freshness with
            {
                Completeness = freshness.Completeness == DataCompleteness.Unavailable
                    ? DataCompleteness.Unavailable
                    : DataCompleteness.Partial,
                Note = freshness.Note is { Length: > 0 } existing
                    ? $"{rangeNote} {existing}"
                    : rangeNote
            };
        }

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

    private async Task<WsEnvelope> HandleAuditAsync(
        WsEnvelope request,
        ClientIdentity identity,
        CancellationToken cancellationToken)
    {
        CapabilityGuard.Require(identity, Capability.AuditRead);

        var query = request.ReadPayload<AuditQueryRequest>() ?? new AuditQueryRequest();
        var result = await _audit.QueryAsync(query.Target, query.Take, cancellationToken);

        var freshness = _freshness.Create(
            "Rejestr audytu (12 miesięcy)",
            result.Metadata,
            TimeSpan.FromSeconds(_options.Freshness.AuditSeconds));

        return WsEnvelope.Create(
            request.Type,
            request.RequestId,
            new AuditQueryResponse(result.Items, result.TotalMatched, freshness));
    }

    private async Task<WsEnvelope> HandleActionPrepareAsync(
        WsEnvelope request,
        ClientIdentity identity,
        CancellationToken cancellationToken)
    {
        var payload = request.ReadPayload<ActionPrepareRequest>();
        if (payload is null)
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Brak payloadu 'action.prepare'.");
        }

        var response = await _actions.PrepareAsync(identity, payload, cancellationToken);
        return WsEnvelope.Create(request.Type, request.RequestId, response);
    }

    private async Task<WsEnvelope> HandleActionExecuteAsync(
        WsEnvelope request,
        ClientIdentity identity,
        CancellationToken cancellationToken)
    {
        var payload = request.ReadPayload<ActionExecuteRequest>();
        if (payload is null)
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Brak payloadu 'action.execute'.");
        }

        var response = await _actions.ExecuteAsync(identity, payload, cancellationToken);
        return WsEnvelope.Create(request.Type, request.RequestId, response);
    }
}
