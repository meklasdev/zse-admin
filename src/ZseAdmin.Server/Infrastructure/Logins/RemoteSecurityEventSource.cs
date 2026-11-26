using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;
using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

// Eventing.Reader jest API wyłącznie Windows. Adapter jest wybierany tylko w trybie
// produkcyjnym na Windows (w development/CI działa źródło Mock), więc ostrzeżenia
// analizatora o zgodności platformowej są tu świadomie wyciszone.
#pragma warning disable CA1416

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>
/// M2 — zdalny odczyt dziennika Security stacji (4624 logowanie, 4634 wylogowanie,
/// 4625 nieudana próba). Wymaga konta usługi w „Event Log Readers” na stacjach.
/// Niedostępność pojedynczej stacji jest raportowana w <see cref="StationLogStatus"/>,
/// dzięki czemu odpowiedź ma <c>completeness = partial</c> zamiast pustej listy.
/// Minimalizacja (RODO): czytamy tylko nazwę konta, stację i czas — bez procesów i treści.
/// </summary>
public sealed class RemoteSecurityEventSource : ILoginEventSource
{
    private const string EventNamespace = "http://schemas.microsoft.com/win/2004/08/events/event";

    private readonly IComputerDirectory _computers;
    private readonly IClock _clock;
    private readonly LoginHistoryOptions _options;

    public RemoteSecurityEventSource(
        IComputerDirectory computers,
        IClock clock,
        LoginHistoryOptions options)
    {
        _computers = computers;
        _clock = clock;
        _options = options;
    }

    public async Task<LoginIngestBatch> ReadSinceAsync(
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var inventory = await _computers.ListAsync(null, cancellationToken).ConfigureAwait(false);
        var events = new List<LoginEventDto>();
        var stations = new List<StationLogStatus>(inventory.Items.Count);

        foreach (var computer in inventory.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsValidHostName(computer.Name))
            {
                stations.Add(new StationLogStatus(
                    computer.Name, false, "Nieprawidłowa nazwa stacji — odczyt dziennika pominięty."));
                continue;
            }

            try
            {
                var fromStation = ReadStation(computer.Name, computer.ClassroomId, sinceUtc, cancellationToken);
                events.AddRange(fromStation);
                stations.Add(new StationLogStatus(computer.Name, true, null));
            }
            catch (Exception ex) when (ex is EventLogException
                                           or UnauthorizedAccessException
                                           or IOException
                                           or System.ComponentModel.Win32Exception
                                           or InvalidOperationException
                                           or PlatformNotSupportedException)
            {
                stations.Add(new StationLogStatus(computer.Name, false, ex.Message));
            }
        }

        return new LoginIngestBatch(events, stations);
    }

    private IReadOnlyList<LoginEventDto> ReadStation(
        string computerName,
        string? classroomId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken)
    {
        var since = sinceUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var xpath =
            $"*[System[(EventID=4624 or EventID=4634 or EventID=4625) " +
            $"and TimeCreated[@SystemTime >= '{since}']]]";

        var query = new EventLogQuery("Security", PathType.LogName, xpath)
        {
            Session = new EventLogSession(computerName)
        };

        var result = new List<LoginEventDto>();
        using var reader = new EventLogReader(query);

        while (!cancellationToken.IsCancellationRequested)
        {
            EventRecord? record = reader.ReadEvent();
            if (record is null)
            {
                break;
            }

            using (record)
            {
                var eventId = (int)record.Id;
                var type = eventId switch
                {
                    4624 => LoginEventType.Logon,
                    4634 => LoginEventType.Logoff,
                    4625 => LoginEventType.FailedLogon,
                    _ => (LoginEventType?)null
                };

                if (type is null)
                {
                    continue;
                }

                var xml = XDocument.Parse(record.ToXml());
                var user = DataValue(xml, "TargetUserName");
                if (string.IsNullOrWhiteSpace(user) || IsSystemAccount(user))
                {
                    continue;
                }

                var occurredAt = ReadTime(xml, record);
                if (occurredAt < sinceUtc)
                {
                    continue;
                }

                var displayName = DataValue(xml, "TargetDisplayName") is { Length: > 0 } name
                    ? name
                    : null;

                var key = $"{computerName}|{user}|{occurredAt.UtcTicks}|{eventId}";
                result.Add(new LoginEventDto(
                    MockLoginEventSource.StableId(key),
                    user,
                    displayName,
                    computerName,
                    occurredAt,
                    type.Value,
                    $"Security log ({eventId})",
                    classroomId));
            }
        }

        return result;
    }

    private static DateTimeOffset ReadTime(XDocument xml, EventRecord record)
    {
        XNamespace ns = EventNamespace;
        var raw = xml.Descendants(ns + "TimeCreated").FirstOrDefault()?.Attribute("SystemTime")?.Value;

        if (raw is { Length: > 0 } &&
            DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        var fallback = record.TimeCreated ?? DateTime.UtcNow;
        return new DateTimeOffset(fallback.ToUniversalTime(), TimeSpan.Zero);
    }

    private static string? DataValue(XDocument xml, string name)
    {
        XNamespace ns = EventNamespace;
        return xml.Descendants(ns + "Data")
            .FirstOrDefault(e => string.Equals(
                e.Attribute("Name")?.Value, name, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();
    }

    private static bool IsSystemAccount(string user) =>
        user.EndsWith("$", StringComparison.Ordinal) ||
        user.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) ||
        user.Equals("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) ||
        user.Equals("NETWORK SERVICE", StringComparison.OrdinalIgnoreCase) ||
        user.Equals("ANONYMOUS LOGON", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidHostName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
        {
            return false;
        }

        return name.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');
    }
}

#pragma warning restore CA1416
