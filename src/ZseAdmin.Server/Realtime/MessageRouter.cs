using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Classrooms;
using ZseAdmin.Server.Security;
using ZseAdmin.Server.Services;

namespace ZseAdmin.Server.Realtime;

/// <summary>
/// Jedno miejsce, w którym wiadomości klienta zamieniają się w odpowiedzi.
/// Każdy przypadek najpierw sprawdza uprawnienie, potem pyta źródło danych,
/// a na końcu dokłada metadane świeżości. Operacje zmieniające stan (M4) zapisują
/// dodatkowo wpis audytowy (M3) z aktorem, celem i wartością przed/po.
/// </summary>
public sealed class MessageRouter
{
    private const int DefaultLoginsTake = 100;
    private const int MaxLoginsTake = 500;
    private const int DefaultAuditTake = 100;
    private const int MaxAuditTake = 200;

    private static readonly Regex ComputerNamePattern =
        new("^[A-Za-z0-9]([A-Za-z0-9-]{0,13}[A-Za-z0-9])?$", RegexOptions.Compiled);

    private static readonly Regex SamAccountNamePattern =
        new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);

    private static readonly Regex GroupNamePattern =
        new("^[A-Za-z0-9 _.-]{1,128}$", RegexOptions.Compiled);

    private readonly IClassroomDirectory _classrooms;
    private readonly IComputerDirectory _computers;
    private readonly ILoginLog _logins;
    private readonly IAdDirectory _users;
    private readonly FreshnessService _freshness;
    private readonly ZseAdminOptions _options;
    private readonly IClock _clock;
    private readonly ClassroomMap _classroomMap;
    private readonly IAuditLog _auditLog;
    private readonly IComputerActionExecutor _computerActions;
    private readonly IAdAccountWriter _accountWriter;
    private readonly ILogger<MessageRouter>? _logger;

    public MessageRouter(
        IClassroomDirectory classrooms,
        IComputerDirectory computers,
        ILoginLog logins,
        IAdDirectory users,
        FreshnessService freshness,
        ZseAdminOptions options,
        IClock clock,
        ClassroomMap classroomMap,
        IAuditLog auditLog,
        IComputerActionExecutor computerActions,
        IAdAccountWriter accountWriter,
        ILogger<MessageRouter>? logger = null)
    {
        _classrooms = classrooms;
        _computers = computers;
        _logins = logins;
        _users = users;
        _freshness = freshness;
        _options = options;
        _clock = clock;
        _classroomMap = classroomMap;
        _auditLog = auditLog;
        _computerActions = computerActions;
        _accountWriter = accountWriter;
        _logger = logger;
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
        CancellationToken cancellationToken = default,
        string? remoteAddress = null)
    {
        try
        {
            return await HandleCoreAsync(request, identity, cancellationToken, remoteAddress).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            // Nieprawidłowy enum lub payload — to błąd żądania, nie „internal”.
            throw new ProtocolException(
                ErrorCode.InvalidRequest,
                $"Nieprawidłowy format payloadu: {ex.Message}");
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
        CancellationToken cancellationToken,
        string? remoteAddress)
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

            case MessageTypes.ComputersAction:
                return await HandleComputerActionAsync(request, identity, remoteAddress, cancellationToken);

            case MessageTypes.UsersResetPassword:
                return await HandleResetPasswordAsync(request, identity, remoteAddress, cancellationToken);

            case MessageTypes.UsersAddToGroup:
                return await HandleAddToGroupAsync(request, identity, remoteAddress, cancellationToken);

            case MessageTypes.AuditQuery:
                return await HandleAuditQueryAsync(request, identity, cancellationToken);

            case MessageTypes.Welcome:
                // Powitanie wysyła wyłącznie serwer po połączeniu; żądanie od klienta to błąd protokołu.
                throw new ProtocolException(
                    ErrorCode.InvalidRequest,
                    "Typ 'welcome' jest zarezerwowany dla serwera.");

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

        // M3: nauczyciel widzi wyłącznie logowania ze stacji w pracowniach mu przypisanych.
        // Żądanie spoza zakresu to forbidden — a nie cicha pusta lista.
        IReadOnlyCollection<string>? allowedClassroomIds = null;
        if (string.Equals(identity.Role, Roles.Teacher, StringComparison.OrdinalIgnoreCase))
        {
            var assigned = _classroomMap.ClassroomIdsForTeacher(identity.Name);
            if (assigned.Count == 0)
            {
                throw new ProtocolException(
                    ErrorCode.Forbidden,
                    $"Konto '{identity.Name}' nie jest przypisane do żadnej pracowni. " +
                    "Poproś administratora o dopisanie konta do 'teachers' w classrooms.json.");
            }

            allowedClassroomIds = assigned;

            if (query.ClassroomId is { } requestedClassroom
                && !assigned.Contains(requestedClassroom.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                throw new ProtocolException(
                    ErrorCode.Forbidden,
                    $"Brak zajęć w pracowni '{requestedClassroom}' dla konta '{identity.Name}'.");
            }

            if (query.ComputerName is { } computerName)
            {
                var computerClassroom = await ResolveComputerClassroomAsync(computerName, cancellationToken)
                    .ConfigureAwait(false);

                if (computerClassroom is null
                    || !assigned.Contains(computerClassroom, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ProtocolException(
                        ErrorCode.Forbidden,
                        $"Komputer '{computerName}' nie należy do pracowni przypisanych do '{identity.Name}'.");
                }

                query = query with { ClassroomId = computerClassroom };
            }
        }

        var result = await _logins.QueryAsync(query, cancellationToken, allowedClassroomIds);
        var freshness = _freshness.Create(
            "Dzienniki zabezpieczeń stacji",
            result.Metadata,
            TimeSpan.FromSeconds(_options.Freshness.LoginsSeconds));

        // Pytanie o zakres poza retencją nie może wyglądać jak „nikt się nie logował”.
        if (query.FromUtc is { } from &&
            _logins.RetentionStartUtc is { } retentionStart &&
            from < retentionStart)
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

    private async Task<WsEnvelope> HandleComputerActionAsync(
        WsEnvelope request,
        ClientIdentity identity,
        string? remoteAddress,
        CancellationToken cancellationToken)
    {
        CapabilityGuard.Require(identity, Capability.ComputersControl);

        var payload = request.ReadPayload<ComputerActionRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.ComputerName))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Pole 'computerName' jest wymagane.");
        }

        var computerName = payload.ComputerName.Trim();
        if (!ComputerNamePattern.IsMatch(computerName))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, $"Nieprawidłowa nazwa komputera: '{computerName}'.");
        }

        if (payload.Action is not (ComputerAction.Restart or ComputerAction.Lock))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, $"Nieznana akcja: '{payload.Action}'. Dozwolone: restart, lock.");
        }

        if (!payload.Confirm)
        {
            throw new ProtocolException(
                ErrorCode.InvalidRequest,
                "Akcja wymaga jawnego potwierdzenia (confirm=true). Klient musi pokazać użytkownikowi prośbę o potwierdzenie.");
        }

        var actionLabel = payload.Action == ComputerAction.Restart ? "restart" : "lock";
        var before = await TryReadComputerStatusAsync(computerName, cancellationToken).ConfigureAwait(false);

        var outcome = payload.Action == ComputerAction.Restart
            ? await _computerActions.RestartAsync(computerName, cancellationToken).ConfigureAwait(false)
            : await _computerActions.LockAsync(computerName, cancellationToken).ConfigureAwait(false);

        if (!outcome.Success)
        {
            await RecordAuditAsync(
                identity,
                remoteAddress,
                Capability.ComputersControl,
                computerName,
                before,
                $"Żądanie: {actionLabel}",
                "failed",
                cancellationToken).ConfigureAwait(false);

            throw new ProtocolException(
                ErrorCode.UpstreamUnavailable,
                $"Akcja '{actionLabel}' na komputerze '{computerName}' nie powiodła się: {outcome.Message}",
                outcome.Message);
        }

        var auditId = await RecordAuditAsync(
            identity,
            remoteAddress,
            Capability.ComputersControl,
            computerName,
            before,
            $"Wydano: {actionLabel}",
            "accepted",
            cancellationToken).ConfigureAwait(false);

        return WsEnvelope.Create(
            request.Type,
            request.RequestId,
            new ComputerActionResponse(computerName, payload.Action, true, outcome.Message, auditId));
    }

    private async Task<WsEnvelope> HandleResetPasswordAsync(
        WsEnvelope request,
        ClientIdentity identity,
        string? remoteAddress,
        CancellationToken cancellationToken)
    {
        CapabilityGuard.Require(identity, Capability.UsersWrite);

        var payload = request.ReadPayload<ResetPasswordRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.SamAccountName))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Pole 'samAccountName' jest wymagane.");
        }

        var sam = payload.SamAccountName.Trim();
        if (!SamAccountNamePattern.IsMatch(sam))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, $"Nieprawidłowa nazwa konta: '{sam}'.");
        }

        if (string.IsNullOrEmpty(payload.NewPassword) || payload.NewPassword.Length < 8)
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Nowe hasło musi mieć co najmniej 8 znaków.");
        }

        if (payload.NewPassword.Length > 256)
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Nowe hasło jest za długie.");
        }

        if (!payload.Confirm)
        {
            throw new ProtocolException(
                ErrorCode.InvalidRequest,
                "Reset hasła wymaga jawnego potwierdzenia (confirm=true).");
        }

        var outcome = await _accountWriter.ResetPasswordAsync(sam, payload.NewPassword, cancellationToken)
            .ConfigureAwait(false);

        if (!outcome.Success)
        {
            await RecordAuditAsync(
                identity,
                remoteAddress,
                Capability.UsersWrite,
                sam,
                "(hasło utajnione)",
                "(hasło zmienione — NIE)",
                "failed",
                cancellationToken).ConfigureAwait(false);

            throw MapAdWriteFailure(outcome);
        }

        var auditId = await RecordAuditAsync(
            identity,
            remoteAddress,
            Capability.UsersWrite,
            sam,
            "(hasło utajnione)",
            "(hasło zmienione)",
            "ok",
            cancellationToken).ConfigureAwait(false);

        return WsEnvelope.Create(
            request.Type,
            request.RequestId,
            new ResetPasswordResponse(sam, outcome.Changed, outcome.Message, auditId));
    }

    private async Task<WsEnvelope> HandleAddToGroupAsync(
        WsEnvelope request,
        ClientIdentity identity,
        string? remoteAddress,
        CancellationToken cancellationToken)
    {
        CapabilityGuard.Require(identity, Capability.UsersWrite);

        var payload = request.ReadPayload<AddToGroupRequest>();
        if (payload is null || string.IsNullOrWhiteSpace(payload.SamAccountName) || string.IsNullOrWhiteSpace(payload.GroupName))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Pola 'samAccountName' i 'groupName' są wymagane.");
        }

        var sam = payload.SamAccountName.Trim();
        var groupName = payload.GroupName.Trim();

        if (!SamAccountNamePattern.IsMatch(sam))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, $"Nieprawidłowa nazwa konta: '{sam}'.");
        }

        if (!GroupNamePattern.IsMatch(groupName))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, $"Nieprawidłowa nazwa grupy: '{groupName}'.");
        }

        if (!payload.Confirm)
        {
            throw new ProtocolException(
                ErrorCode.InvalidRequest,
                "Dodanie do grupy wymaga jawnego potwierdzenia (confirm=true).");
        }

        var outcome = await _accountWriter.AddUserToGroupAsync(sam, groupName, cancellationToken)
            .ConfigureAwait(false);

        if (!outcome.Success)
        {
            await RecordAuditAsync(
                identity,
                remoteAddress,
                Capability.UsersWrite,
                $"{sam} → {groupName}",
                outcome.Before,
                "(bez zmiany)",
                "failed",
                cancellationToken).ConfigureAwait(false);

            throw MapAdWriteFailure(outcome);
        }

        var result = outcome.Changed ? "ok" : "nochange";
        var auditId = await RecordAuditAsync(
            identity,
            remoteAddress,
            Capability.UsersWrite,
            $"{sam} → {groupName}",
            outcome.Before,
            outcome.After,
            result,
            cancellationToken).ConfigureAwait(false);

        return WsEnvelope.Create(
            request.Type,
            request.RequestId,
            new AddToGroupResponse(sam, groupName, outcome.Changed, outcome.Message, auditId));
    }

    private async Task<WsEnvelope> HandleAuditQueryAsync(
        WsEnvelope request,
        ClientIdentity identity,
        CancellationToken cancellationToken)
    {
        CapabilityGuard.Require(identity, Capability.AuditRead);

        var query = request.ReadPayload<AuditQueryRequest>() ?? new AuditQueryRequest();
        var take = Math.Clamp(query.Take <= 0 ? DefaultAuditTake : query.Take, 1, MaxAuditTake);

        var entries = await _auditLog.QueryAsync(
            string.IsNullOrWhiteSpace(query.Target) ? null : query.Target.Trim(),
            string.IsNullOrWhiteSpace(query.Capability) ? null : query.Capability.Trim(),
            take,
            cancellationToken).ConfigureAwait(false);

        var items = entries
            .Select(e => new AuditEntryDto(
                e.Id,
                e.TimestampUtc,
                e.Actor,
                e.Role,
                e.Capability,
                e.Target,
                e.Before,
                e.After,
                e.Result,
                e.RemoteAddress))
            .ToList();

        var freshness = _freshness.Create(
            "Dziennik audytu (lokalny)",
            QueryMetadata.Complete(_clock.UtcNow),
            TimeSpan.FromSeconds(_options.Freshness.DirectorySeconds));

        return WsEnvelope.Create(request.Type, request.RequestId, new AuditResponse(items, freshness));
    }

    private async Task<string> TryReadComputerStatusAsync(string computerName, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _computers.ListAsync(null, cancellationToken).ConfigureAwait(false);
            var computer = result.Items.FirstOrDefault(c =>
                string.Equals(c.Name, computerName, StringComparison.OrdinalIgnoreCase));

            if (computer is null)
            {
                return "nieznany (brak w inwentarzu)";
            }

            var observed = computer.StatusObservedAtUtc is { } at ? at.ToString("yyyy-MM-dd HH:mm") : "brak";
            return $"status={computer.Status.ToString().ToLowerInvariant()} (obserwacja {observed} UTC)";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Nie udało się odczytać stanu komputera {Computer} na potrzeby audytu.", computerName);
            return "(brak danych)";
        }
    }

    private async Task<string?> ResolveComputerClassroomAsync(string computerName, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _computers.ListAsync(null, cancellationToken).ConfigureAwait(false);
            return result.Items
                .FirstOrDefault(c => string.Equals(c.Name, computerName, StringComparison.OrdinalIgnoreCase))
                ?.ClassroomId;
        }
        catch (SourceUnavailableException)
        {
            // Bez inwentarza nie damy rady zweryfikować zakresu — odmowa (fail-closed).
            throw new ProtocolException(
                ErrorCode.UpstreamUnavailable,
                "Nie można zweryfikować przynależności komputera do pracowni (inwentarz niedostępny). Spróbuj ponownie.");
        }
    }

    /// <summary>
    /// Zapisuje wpis audytowy. Błąd zapisu wywraca operację — lepiej nie udawać udanej
    /// zmiany bez rozliczalności, niż po cichu ją wykonać.
    /// </summary>
    private async Task<Guid> RecordAuditAsync(
        ClientIdentity identity,
        string? remoteAddress,
        string capability,
        string target,
        string? before,
        string? after,
        string result,
        CancellationToken cancellationToken)
    {
        var entry = new AuditEntry(
            Guid.NewGuid(),
            _clock.UtcNow,
            identity.Name,
            identity.Role,
            capability,
            target,
            before,
            after,
            result,
            remoteAddress);

        try
        {
            await _auditLog.RecordAsync(entry, cancellationToken).ConfigureAwait(false);
            return entry.Id;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Nie udało się zapisać audytu dla {Target} ({Capability}).", target, capability);
            throw new ProtocolException(
                ErrorCode.Internal,
                "Nie udało się zapisać wpisu audytowego. Operacja nie została zaraportowana jako udana.");
        }
    }

    private static ProtocolException MapAdWriteFailure(AdWriteOutcome outcome) =>
        outcome.ErrorKind switch
        {
            AdWriteErrorKind.NotFound => new ProtocolException(ErrorCode.NotFound, outcome.Message),
            AdWriteErrorKind.Rejected => new ProtocolException(
                ErrorCode.UpstreamUnavailable,
                $"Active Directory odrzuciło operację: {outcome.Message}",
                outcome.Message),
            _ => new ProtocolException(
                ErrorCode.UpstreamUnavailable,
                $"Operacja na Active Directory nie powiodła się: {outcome.Message}",
                outcome.Message)
        };
}
