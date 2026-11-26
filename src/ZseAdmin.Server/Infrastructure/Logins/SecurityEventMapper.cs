using System.Security.Cryptography;
using System.Text;
using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>
/// Mapowanie surowych zdarzeń zabezpieczeń (4624/4634/4625) na <see cref="LoginEventDto"/>.
/// Oddzielone od odczytu dziennika, żeby dało się je w pełni przetestować bez stacji.
/// Zdarzenia kont systemowych i maszynowych (SYSTEM, LOCAL SERVICE, konta $) są pomijane —
/// historia dotyczy ludzi przy stacjach.
/// </summary>
public static class SecurityEventMapper
{
    /// <summary>Mapowanie id zdarzenia na rodzaj + audytowalne źródło.</summary>
    public static bool TryMapEventType(int eventId, out LoginEventType type, out string source)
    {
        switch (eventId)
        {
            case 4624:
                type = LoginEventType.Logon;
                source = "Security log (4624)";
                return true;
            case 4634:
                type = LoginEventType.Logoff;
                source = "Security log (4634)";
                return true;
            case 4625:
                type = LoginEventType.FailedLogon;
                source = "Security log (4625)";
                return true;
            default:
                type = default;
                source = string.Empty;
                return false;
        }
    }

    /// <summary>Czy pominąć zdarzenie: konta systemowe, maszynowe (końcówka „$”) i anonimowe.</summary>
    public static bool ShouldSkipPrincipal(string? userSamAccountName)
    {
        if (string.IsNullOrWhiteSpace(userSamAccountName))
        {
            return true;
        }

        var name = userSamAccountName.Trim();

        if (name.EndsWith('$'))
        {
            return true;
        }

        return name.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)
            || name.Equals("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NETWORK SERVICE", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ANONYMOUS LOGON", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Zamienia surowe zdarzenie na DTO albo <c>null</c> (pomijamy).</summary>
    public static LoginEventDto? Map(
        SecurityLogEvent entry,
        string computerName,
        string? classroomId)
    {
        if (entry.RecordId <= 0)
        {
            // Bez RecordId nie da się deduplikować po ponownym uruchomieniu — pomijamy.
            return null;
        }

        if (ShouldSkipPrincipal(entry.UserSamAccountName))
        {
            return null;
        }

        if (!TryMapEventType(entry.EventId, out var type, out var source))
        {
            return null;
        }

        return new LoginEventDto(
            Id: DeterministicId(computerName, entry.RecordId),
            UserSamAccountName: entry.UserSamAccountName.Trim(),
            UserDisplayName: null,
            ComputerName: computerName,
            OccurredAtUtc: entry.OccurredAtUtc,
            Type: type,
            Source: source,
            ClassroomId: classroomId);
    }

    /// <summary>Deterministyczny identyfikator zdarzenia (stabilny po restarcie serwera).</summary>
    public static Guid DeterministicId(string computerName, long recordId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{computerName}|{recordId}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
