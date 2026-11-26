using System.Diagnostics.Eventing.Reader;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Logins;

/// <summary>
/// Odczyt dziennika zabezpieczeń per stacja (tylko Windows). Każda stacja jest czy-
/// tana niezależnie — jedna niedostępna stacja nie zatrzymuje pozostałych. Całość
/// mieści się w <paramref name="budget"/>; po jego przekroczeniu stacje bez odpowiedzi
/// są zgłaszane jako niedostępne (dane niepełne, a nie „brak logowań”).
/// </summary>
internal static class WindowsSecurityEventReader
{
    private const int MaxEventsPerStation = 1000;

    public static async Task<IReadOnlyList<StationSecurityLogResult>> ReadAsync(
        IReadOnlyList<SecurityLogStationQuery> queries,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        // Od czasu żądania nie zależymy od tokenu anulowania HTTP — czytamy dalej
        // w tle, ale wynik ignorujemy po przekroczeniu budżetu.
        var tasks = queries
            .Select(q => Task.Run(() => ReadStation(q), CancellationToken.None))
            .ToList();

        var all = Task.WhenAll(tasks);
        var deadline = Task.Delay(budget, cancellationToken);
        var finished = await Task.WhenAny(all, deadline).ConfigureAwait(false);

        if (finished == all)
        {
            return all.Result;
        }

        var partialResults = new List<StationSecurityLogResult>(queries.Count);
        foreach (var task in tasks)
        {
            if (task.IsCompletedSuccessfully)
            {
                partialResults.Add(task.Result);
            }
            else
            {
                var index = tasks.IndexOf(task);
                partialResults.Add(new StationSecurityLogResult(
                    queries[index].ComputerName,
                    false,
                    $"Przekroczono budżet odczytu ({budget.TotalSeconds:0} s) — stacja nie odpowiedziała.",
                    Array.Empty<SecurityLogEvent>()));
            }
        }

        return partialResults;
    }

    private static StationSecurityLogResult ReadStation(SecurityLogStationQuery query)
    {
        try
        {
            var notBefore = query.NotBeforeUtc.ToUniversalTime();
            var systemTime = notBefore.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var xpath =
                $"*[System[(EventID=4624 or EventID=4634 or EventID=4625) and TimeCreated[@SystemTime>='{systemTime}']]]";

            var logQuery = new EventLogQuery(query.ComputerName, PathType.LogName, xpath)
            {
                ReverseDirection = true,
                TolerateQueryErrors = true
            };

            var events = new List<SecurityLogEvent>();

            using (var reader = new EventLogReader(logQuery))
            {
                while (events.Count < MaxEventsPerStation)
                {
                    using var record = reader.ReadEvent();
                    if (record is null)
                    {
                        break;
                    }

                    var user = TryGetUserSamAccountName(record);
                    if (user is null)
                    {
                        continue;
                    }

                    var occurred = record.TimeCreated is { } time
                        ? new DateTimeOffset(time, TimeZoneInfo.Local.GetUtcOffset(time)).ToUniversalTime()
                        : DateTimeOffset.UtcNow;

                    events.Add(new SecurityLogEvent(
                        record.RecordId ?? 0,
                        occurred,
                        user,
                        (int)record.Id));
                }
            }

            return new StationSecurityLogResult(query.ComputerName, true, null, events);
        }
        catch (UnauthorizedAccessException)
        {
            return new StationSecurityLogResult(
                query.ComputerName,
                false,
                "Brak uprawnień do dziennika zabezpieczeń (konto usługi w grupie „Czytelnicy dzienników zdarzeń” na stacji).",
                Array.Empty<SecurityLogEvent>());
        }
        catch (EventLogNotFoundException)
        {
            return new StationSecurityLogResult(
                query.ComputerName,
                false,
                "Dziennik zabezpieczeń nie jest dostępny na stacji (stacja wyłączona lub wyczyszczony dziennik).",
                Array.Empty<SecurityLogEvent>());
        }
        catch (Exception ex)
        {
            return new StationSecurityLogResult(
                query.ComputerName,
                false,
                $"Odczyt dziennika nie powiódł się: {ex.Message}",
                Array.Empty<SecurityLogEvent>());
        }
    }

    /// <summary>
    /// W 4624/4625/4634 <c>TargetUserName</c> siedzi na indeksie 5 (Windows 10/11, Server 2016+).
    /// Gdy układ jest nieoczekiwany, ratuje nas indeks 1 (SubjectUserName).
    /// </summary>
    private static string? TryGetUserSamAccountName(EventRecord record)
    {
        var properties = record.Properties;

        var target = ReadProperty(properties, 5);
        if (!string.IsNullOrWhiteSpace(target))
        {
            return target;
        }

        var subject = ReadProperty(properties, 1);
        return string.IsNullOrWhiteSpace(subject) ? null : subject;
    }

    private static string? ReadProperty(IList<EventProperty> properties, int index) =>
        index >= 0 && index < properties.Count
            ? (properties[index].Value as string)?.Trim()
            : null;
}
