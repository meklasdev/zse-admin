using System.Security.Cryptography;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Realtime;
using ZseAdmin.Server.Security;

namespace ZseAdmin.Server.Services;

/// <summary>Jednorazowe potwierdzenie akcji M4, związane z aktorem, celem i czasem.</summary>
public sealed record PendingAction(
    string Token,
    AdminActionKind Action,
    string Target,
    string Capability,
    string Actor,
    string Role,
    string Before,
    string? GroupName,
    string? Reason,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Przygotowanie akcji nie zmienia stanu. Dopiero <c>action.execute</c> z ważnym,
/// jednorazowym tokenem może wywołać zapis. Token wygasa po
/// <c>ZseAdmin:Actions:ConfirmationSeconds</c> i nie przechodzi na inną sesję.
/// </summary>
public sealed class ConfirmationStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, PendingAction> _pending = new(StringComparer.Ordinal);
    private readonly IClock _clock;
    private readonly ActionOptions _options;

    public ConfirmationStore(IClock clock, ActionOptions options)
    {
        _clock = clock;
        _options = options;
    }

    public PendingAction Issue(
        ClientIdentity identity,
        AdminActionKind action,
        string target,
        string capability,
        string before,
        string? groupName,
        string? reason)
    {
        var lifetime = TimeSpan.FromSeconds(Math.Clamp(_options.ConfirmationSeconds, 10, 900));
        var item = new PendingAction(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
            action,
            target,
            capability,
            identity.Name,
            identity.Role,
            before,
            groupName,
            reason,
            _clock.UtcNow.Add(lifetime));

        lock (_sync)
        {
            Prune();
            _pending[item.Token] = item;
        }

        return item;
    }

    public PendingAction Consume(string token, ClientIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ProtocolException(ErrorCode.InvalidRequest, "Brak tokenu potwierdzenia akcji.");
        }

        lock (_sync)
        {
            Prune();

            if (!_pending.Remove(token.Trim(), out var item))
            {
                throw new ProtocolException(
                    ErrorCode.InvalidRequest,
                    "Potwierdzenie wygasło, zostało już użyte albo nie istnieje. Przygotuj akcję ponownie.");
            }

            if (!string.Equals(item.Actor, identity.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new ProtocolException(
                    ErrorCode.Forbidden,
                    "Potwierdzenie zostało wydane dla innego użytkownika.");
            }

            return item;
        }
    }

    public void Cancel(string token)
    {
        lock (_sync)
        {
            _pending.Remove(token, out _);
        }
    }

    private void Prune()
    {
        var now = _clock.UtcNow;
        foreach (var expired in _pending.Where(p => p.Value.ExpiresAtUtc <= now).Select(p => p.Key).ToList())
        {
            _pending.Remove(expired);
        }
    }
}
