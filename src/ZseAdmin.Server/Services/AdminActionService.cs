using System.Security.Cryptography;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Realtime;
using ZseAdmin.Server.Security;

namespace ZseAdmin.Server.Services;

/// <summary>
/// M4 — jedyna droga zmiany stanu. Dwa kroki (prepare → execute), potwierdzenie
/// jednorazowe, audyt sukcesu i porażki. Błąd źródła nigdy nie wraca jako „sukces”:
/// klient dostaje <c>error: upstreamUnavailable</c> z nazwą źródła.
/// </summary>
public sealed class AdminActionService
{
    private readonly IComputerControl _computerControl;
    private readonly IAdWriteService _adWrite;
    private readonly IAuditLog _audit;
    private readonly ConfirmationStore _confirmations;
    private readonly IClock _clock;
    private readonly SecurityOptions _security;
    private readonly ActionOptions _options;

    public AdminActionService(
        IComputerControl computerControl,
        IAdWriteService adWrite,
        IAuditLog audit,
        ConfirmationStore confirmations,
        IClock clock,
        SecurityOptions security,
        ActionOptions options)
    {
        _computerControl = computerControl;
        _adWrite = adWrite;
        _audit = audit;
        _confirmations = confirmations;
        _clock = clock;
        _security = security;
        _options = options;
    }

    public async Task<ActionPrepareResponse> PrepareAsync(
        ClientIdentity identity,
        ActionPrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Action))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Nieznany rodzaj akcji.");
        }

        var target = ValidateTarget(request.Target);
        var groupName = string.IsNullOrWhiteSpace(request.GroupName) ? null : request.GroupName.Trim();
        var (capability, isAdAction) = CapabilityFor(request.Action);

        CapabilityGuard.Require(identity, capability);

        if (request.Action == AdminActionKind.AddToGroup && groupName is null)
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Dla 'AddToGroup' pole 'groupName' jest wymagane.");
        }

        if (request.Action == AdminActionKind.ResetPassword &&
            _security.RequireSecureTransport && !identity.IsSecureTransport)
        {
            throw new ProtocolException(
                ErrorCode.Forbidden,
                "Reset hasła wymaga połączenia wss — odrzucono połączenie niezabezpieczone.");
        }

        string before;
        try
        {
            before = isAdAction
                ? await _adWrite.DescribeUserAsync(target, cancellationToken).ConfigureAwait(false)
                : await _computerControl.DescribeAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (ActionTargetNotFoundException ex)
        {
            throw new ProtocolException(ErrorCode.NotFound, ex.Message);
        }
        catch (ActionFailedException ex)
        {
            throw new ProtocolException(
                ErrorCode.UpstreamUnavailable,
                $"Nie udało się odczytać stanu obiektu ({ex.SourceName}): {ex.Message}",
                ex.Message);
        }
        catch (SourceUnavailableException ex)
        {
            throw new ProtocolException(
                ErrorCode.UpstreamUnavailable,
                $"Nie udało się odczytać stanu obiektu ({ex.SourceName}): {ex.Message}",
                ex.Message);
        }

        var pending = _confirmations.Issue(
            identity,
            request.Action,
            target,
            capability,
            before,
            groupName,
            request.Reason);

        return new ActionPrepareResponse(
            pending.Token,
            request.Action,
            target,
            capability,
            Description(request.Action, target, groupName),
            before,
            Warnings(request.Action, groupName),
            pending.ExpiresAtUtc);
    }

    public async Task<ActionExecuteResponse> ExecuteAsync(
        ClientIdentity identity,
        ActionExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Confirm)
        {
            throw new ProtocolException(
                ErrorCode.InvalidRequest,
                "Akcja wymaga jawnego potwierdzenia (confirm = true).");
        }

        var pending = _confirmations.Consume(request.ConfirmationToken, identity);

        // Uprawnienie sprawdzamy ponownie w momencie wykonania, nie tylko przy przygotowaniu.
        CapabilityGuard.Require(identity, pending.Capability);

        if (!_options.AllowWrites)
        {
            throw new ProtocolException(
                ErrorCode.Forbidden,
                "Zapis jest wyłączony konfiguracją (ZseAdmin:Actions:AllowWrites = false).");
        }

        string? temporaryPassword = null;

        try
        {
            ActionChange change;

            switch (pending.Action)
            {
                case AdminActionKind.RestartComputer:
                    change = await _computerControl
                        .RestartAsync(pending.Target, cancellationToken).ConfigureAwait(false);
                    break;

                case AdminActionKind.LockComputer:
                    change = await _computerControl
                        .LockAsync(pending.Target, cancellationToken).ConfigureAwait(false);
                    break;

                case AdminActionKind.ResetPassword:
                    temporaryPassword = TemporaryPasswordGenerator.Generate();
                    change = await _adWrite
                        .ResetPasswordAsync(pending.Target, temporaryPassword, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case AdminActionKind.AddToGroup:
                    change = await _adWrite
                        .AddToGroupAsync(pending.Target, pending.GroupName!, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                default:
                    throw new ProtocolException(ErrorCode.InvalidRequest, "Nieznany rodzaj akcji.");
            }

            var successAudit = await _audit.RecordAsync(
                new AuditRecord(
                    pending.Actor,
                    pending.Role,
                    pending.Capability,
                    pending.Action.ToString(),
                    pending.Target,
                    change.Before,
                    change.After,
                    "success",
                    identity.RemoteAddress,
                    BuildDetail(pending, change.Message, request.Reason)),
                cancellationToken).ConfigureAwait(false);

            return new ActionExecuteResponse(
                true,
                pending.Action,
                pending.Target,
                change.Message,
                change.Before,
                change.After,
                successAudit.Id.ToString(),
                temporaryPassword,
                new DataFreshnessDto(
                    "Operacja serwera", _clock.UtcNow, false, DataCompleteness.Complete, null));
        }
        catch (ActionTargetNotFoundException ex)
        {
            await RecordFailureAsync(
                pending, identity, ex.Message, request.Reason, cancellationToken).ConfigureAwait(false);

            throw new ProtocolException(ErrorCode.NotFound, ex.Message);
        }
        catch (ActionFailedException ex)
        {
            await RecordFailureAsync(
                pending, identity, ex.Message, request.Reason, cancellationToken).ConfigureAwait(false);

            throw new ProtocolException(
                ErrorCode.UpstreamUnavailable,
                $"Akcja '{pending.Action}' nie powiodła się ({ex.SourceName}): {ex.Message}",
                ex.Message);
        }
        catch (SourceUnavailableException ex)
        {
            await RecordFailureAsync(
                pending, identity, ex.Message, request.Reason, cancellationToken).ConfigureAwait(false);

            throw new ProtocolException(
                ErrorCode.UpstreamUnavailable,
                $"Akcja '{pending.Action}' nie powiodła się ({ex.SourceName}): {ex.Message}",
                ex.Message);
        }
    }

    private async Task RecordFailureAsync(
        PendingAction pending,
        ClientIdentity identity,
        string reason,
        string? executeReason,
        CancellationToken cancellationToken)
    {
        await _audit.RecordAsync(
            new AuditRecord(
                pending.Actor,
                pending.Role,
                pending.Capability,
                pending.Action.ToString(),
                pending.Target,
                pending.Before,
                pending.Before,
                "failed",
                identity.RemoteAddress,
                $"{BuildDetail(pending, null, executeReason)} Błąd źródła: {reason}"),
            cancellationToken).ConfigureAwait(false);
    }

    private static string BuildDetail(
        PendingAction pending,
        string? sourceMessage,
        string? executeReason = null)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(pending.Reason))
        {
            parts.Add($"Powód: {pending.Reason.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(executeReason))
        {
            parts.Add($"Powód potwierdzenia: {executeReason.Trim()}");
        }

        if (pending.GroupName is { Length: > 0 } group)
        {
            parts.Add($"Grupa: {group}");
        }

        if (!string.IsNullOrWhiteSpace(sourceMessage))
        {
            parts.Add(sourceMessage.Trim());
        }

        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    private static (string Capability, bool IsAdAction) CapabilityFor(AdminActionKind action) =>
        action switch
        {
            AdminActionKind.RestartComputer or AdminActionKind.LockComputer =>
                (Capability.ComputersControl, false),
            AdminActionKind.ResetPassword or AdminActionKind.AddToGroup =>
                (Capability.UsersWrite, true),
            _ => throw new ProtocolException(ErrorCode.InvalidRequest, "Nieznany rodzaj akcji.")
        };

    private IReadOnlyList<string> Warnings(AdminActionKind action, string? groupName)
    {
        var warnings = new List<string>
        {
            "Operacja zostanie zapisana w audycie z aktorem, celem i wartością przed/po."
        };

        switch (action)
        {
            case AdminActionKind.RestartComputer:
                warnings.Add(
                    $"Stacja zrestartuje się za {Math.Clamp(_options.RestartDelaySeconds, 0, 300)} s — upewnij się, że nikt nie pracuje.");
                break;
            case AdminActionKind.LockComputer:
                warnings.Add("Zablokowanie ekranu wymaga WinRM na stacji; brak dostępu będzie zgłoszony jako błąd źródła.");
                break;
            case AdminActionKind.ResetPassword:
                warnings.Add("Hasło tymczasowe pojawi się jednorazowo po wykonaniu i nie trafi do audytu.");
                warnings.Add("Użytkownik będzie musiał zmienić hasło przy najbliższym logowaniu.");
                break;
            case AdminActionKind.AddToGroup:
                warnings.Add($"Konto zostanie dodane do grupy '{groupName}'.");
                break;
        }

        return warnings;
    }

    private static string Description(AdminActionKind action, string target, string? groupName) =>
        action switch
        {
            AdminActionKind.RestartComputer => $"Restart stacji {target}",
            AdminActionKind.LockComputer => $"Zablokowanie ekranu stacji {target}",
            AdminActionKind.ResetPassword => $"Reset hasła konta {target}",
            AdminActionKind.AddToGroup => $"Dodanie konta {target} do grupy {groupName}",
            _ => "Operacja"
        };

    private static string ValidateTarget(string? raw)
    {
        var target = (raw ?? string.Empty).Trim();
        if (target.Length is 0 or > 256 || target.Any(char.IsControl))
        {
            throw new ProtocolException(
                ErrorCode.InvalidRequest,
                "Pole 'target' jest wymagane (1–256 znaków, bez znaków sterujących).");
        }

        return target;
    }
}

/// <summary>Hasło tymczasowe generowane wyłącznie po stronie serwera (M4).</summary>
public static class TemporaryPasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%";
    private const string All = Upper + Lower + Digits + Symbols;

    public static string Generate(int length = 14)
    {
        var size = Math.Clamp(length, 12, 32);
        var chars = new List<char>(size)
        {
            Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols)
        };

        while (chars.Count < size)
        {
            chars.Add(Pick(All));
        }

        for (var index = chars.Count - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (chars[index], chars[swap]) = (chars[swap], chars[index]);
        }

        return new string(chars.ToArray());
    }

    private static char Pick(string alphabet) =>
        alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
