using System.Security.Cryptography;
using System.Text;
using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Mock;

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>
/// Źródło M2 dla development/CI: deterministyczna historia + jedno nowe logowanie
/// w każdym cyklu (do sprawdzenia push) i 2 z 6 stacji bez dostępu do dziennika
/// (do sprawdzenia kompletności). Nie udaje prawdziwych danych — /healthz to pokazuje.
/// </summary>
public sealed class MockLoginEventSource : ILoginEventSource
{
    private readonly IClock _clock;
    private int _poll;

    public MockLoginEventSource(IClock clock, LoginHistoryOptions options)
    {
        _clock = clock;
        _ = options;
    }

    public Task<LoginIngestBatch> ReadSinceAsync(
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.UtcNow;
        var computers = SchoolSeed.Computers(now);
        var classroomById = computers.ToDictionary(
            c => c.Name,
            c => c.ClassroomId,
            StringComparer.OrdinalIgnoreCase);

        var events = new List<LoginEventDto>();

        if (_poll == 0)
        {
            foreach (var seed in SchoolSeed.Logins(now))
            {
                var loginEvent = seed with { ClassroomId = classroomById.GetValueOrDefault(seed.ComputerName) };
                if (loginEvent.OccurredAtUtc >= sinceUtc)
                {
                    events.Add(loginEvent);
                }
            }
        }

        // „Uczeń zalogował się na stacji” — nowe zdarzenie w każdym cyklu.
        var current = Create(
            "j.kowalski", "Jan Kowalski", "KS-101-01", now,
            LoginEventType.Logon, "Security log (4624)",
            classroomById.GetValueOrDefault("KS-101-01"));

        if (current.OccurredAtUtc >= sinceUtc)
        {
            events.Add(current);
        }

        _poll++;

        var stations = computers
            .Where(c => string.Equals(c.ClassroomId, "prac-101", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .Select((computer, index) => new StationLogStatus(
                computer.Name,
                Available: index < 4,
                Reason: index < 4 ? null : "Symulowany brak uprawnień do dziennika (Event Log Readers)."))
            .ToList();

        return Task.FromResult(new LoginIngestBatch(events, stations));
    }

    public static LoginEventDto Create(
        string user,
        string? displayName,
        string computerName,
        DateTimeOffset occurredAtUtc,
        LoginEventType type,
        string source,
        string? classroomId)
    {
        var key = $"{computerName}|{user}|{occurredAtUtc.UtcTicks}|{(int)type}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));

        return new LoginEventDto(
            new Guid(hash),
            user,
            displayName,
            computerName,
            occurredAtUtc,
            type,
            source,
            classroomId);
    }

    /// <summary>Stabilny identyfikator rekordu dziennika — do użytku przez realne źródła.</summary>
    public static Guid StableId(string recordKey)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(recordKey));
        return new Guid(hash);
    }
}
