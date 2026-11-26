using System.Text.Json;
using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>
/// Wariant M2 z Windows Event Forwarding: subskrypcja na serwerze zapisuje zdarzenia
/// 4624/4634/4625 do plików <c>*.jsonl</c>, a <c>stations.json</c> jest heartbeatem stacji.
/// Brak pliku katalogu lub świeżego heartbeatu jest jawnie raportowany jako niedostępność,
/// a nie jako „brak logowań”.
/// </summary>
public sealed class WefFileEventSource : ILoginEventSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IClock _clock;
    private readonly LoginHistoryOptions _options;

    public WefFileEventSource(IClock clock, LoginHistoryOptions options)
    {
        _clock = clock;
        _options = options;
    }

    public async Task<LoginIngestBatch> ReadSinceAsync(
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(_options.WefDirectory) ? "wef" : _options.WefDirectory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"Brak katalogu WEF '{directory}' (ZseAdmin:LoginHistory:WefDirectory).");
        }

        var events = new List<LoginEventDto>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var line in await File.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                WefRecord? record;
                try
                {
                    record = JsonSerializer.Deserialize<WefRecord>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (record is null || record.OccurredAtUtc is not { } occurredAt || occurredAt < sinceUtc)
                {
                    continue;
                }

                var type = record.EventId switch
                {
                    4624 => LoginEventType.Logon,
                    4634 => LoginEventType.Logoff,
                    4625 => LoginEventType.FailedLogon,
                    _ => (LoginEventType?)null
                };

                if (type is null || string.IsNullOrWhiteSpace(record.ComputerName) ||
                    string.IsNullOrWhiteSpace(record.UserSamAccountName))
                {
                    continue;
                }

                var source = string.IsNullOrWhiteSpace(record.Source)
                    ? $"Security log ({record.EventId})"
                    : record.Source;

                events.Add(MockLoginEventSource.Create(
                    record.UserSamAccountName.Trim(),
                    record.UserDisplayName,
                    record.ComputerName.Trim(),
                    occurredAt.ToUniversalTime(),
                    type.Value,
                    source,
                    record.ClassroomId));
            }
        }

        var stations = ReadStations(directory).ToList();
        if (stations.Count == 0)
        {
            stations = events
                .Select(e => e.ComputerName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => new StationLogStatus(name, true, null))
                .ToList();
        }

        return new LoginIngestBatch(events, stations);
    }

    private List<StationLogStatus> ReadStations(string directory)
    {
        var result = new List<StationLogStatus>();
        var path = Path.Combine(directory, "stations.json");
        if (!File.Exists(path))
        {
            return result;
        }

        List<WefStation>? stations;
        try
        {
            stations = JsonSerializer.Deserialize<List<WefStation>>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return result;
        }

        if (stations is null)
        {
            return result;
        }

        var staleAfter = TimeSpan.FromSeconds(Math.Max(30, _options.PollSeconds) * 2);
        var now = _clock.UtcNow;

        foreach (var station in stations)
        {
            if (string.IsNullOrWhiteSpace(station.ComputerName))
            {
                continue;
            }

            var available = station.LastSeenUtc is { } lastSeen && now - lastSeen.ToUniversalTime() <= staleAfter;
            result.Add(new StationLogStatus(
                station.ComputerName.Trim(),
                available,
                available ? null : "Brak świeżego heartbeatu WEF ze stacji."));
        }

        return result;
    }

    private sealed class WefRecord
    {
        public string? ComputerName { get; set; }
        public string? UserSamAccountName { get; set; }
        public string? UserDisplayName { get; set; }
        public string? ClassroomId { get; set; }
        public DateTimeOffset? OccurredAtUtc { get; set; }
        public int EventId { get; set; }
        public string? RecordId { get; set; }
        public string? Source { get; set; }
    }

    private sealed class WefStation
    {
        public string? ComputerName { get; set; }
        public DateTimeOffset? LastSeenUtc { get; set; }
    }
}
