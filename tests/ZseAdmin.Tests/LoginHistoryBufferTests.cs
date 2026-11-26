using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Logins;
using Xunit;

namespace ZseAdmin.Tests;

/// <summary>Kryteria akceptacji M2: świeżość, kompletność, zakres i retencja 30 dni.</summary>
public sealed class LoginHistoryBufferTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Nowe_logowanie_jest_widoczne_po_jednym_cyklu_ingestu()
    {
        var clock = new FakeClock(Now);
        var loginEvent = Event(clock.UtcNow.AddSeconds(-30), "j.kowalski", "KS-101-01", "prac-101");
        var buffer = Buffer(clock, new[] { loginEvent }, Stations(6, unavailable: 0));

        var added = await buffer.IngestAsync();
        var result = await buffer.QueryAsync(new LoginQueryRequest(Take: 10));

        Assert.Single(added);
        Assert.Single(result.Items);
        Assert.Equal("j.kowalski", result.Items[0].UserSamAccountName);
        Assert.Equal(DataCompleteness.Complete, result.Metadata.Completeness);
        Assert.NotNull(result.Metadata.ObservedAtUtc);
    }

    [Fact]
    public async Task Dwie_z_szesciu_stacji_niedostepnych_daja_partial_z_liczba_stacji()
    {
        var clock = new FakeClock(Now);
        var buffer = Buffer(clock, Array.Empty<LoginEventDto>(), Stations(6, unavailable: 2));

        await buffer.IngestAsync();
        var result = await buffer.QueryAsync(new LoginQueryRequest());

        Assert.Equal(DataCompleteness.Partial, result.Metadata.Completeness);
        Assert.Contains("2 z 6 stacji", result.Metadata.Note!);
        Assert.Contains("Brak dostępu", result.Metadata.Note!);
    }

    [Fact]
    public async Task Zakres_przed_retencja_ma_jawny_komunikat_a_nie_pusta_liste_bez_note()
    {
        var clock = new FakeClock(Now);
        var loginEvent = Event(clock.UtcNow.AddHours(-1), "j.kowalski", "KS-101-01", "prac-101");
        var buffer = Buffer(clock, new[] { loginEvent }, Stations(2, unavailable: 0));

        await buffer.IngestAsync();
        var result = await buffer.QueryAsync(new LoginQueryRequest(FromUtc: Now.AddDays(-40)));

        Assert.Equal(DataCompleteness.Partial, result.Metadata.Completeness);
        Assert.Contains("retencj", result.Metadata.Note!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30 dni", result.Metadata.Note!);
    }

    [Fact]
    public async Task Po_30_dniach_zdarzenia_zostaja_fizycznie_usuniete()
    {
        var clock = new FakeClock(Now);
        var loginEvent = Event(clock.UtcNow.AddHours(-1), "j.kowalski", "KS-101-01", "prac-101");
        var buffer = Buffer(clock, new[] { loginEvent }, Stations(2, unavailable: 0));

        await buffer.IngestAsync();
        Assert.Equal(1, buffer.Count);

        clock.Advance(TimeSpan.FromDays(31));
        await buffer.IngestAsync(); // źródło zwraca ten sam stary rekord; bufor go odrzuci
        var result = await buffer.QueryAsync(new LoginQueryRequest());

        Assert.Equal(0, buffer.Count);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalMatched);
    }

    [Fact]
    public async Task Bufor_filtruje_po_komputerze_uzytkowniku_czasie_i_pracowni()
    {
        var clock = new FakeClock(Now);
        var events = new[]
        {
            Event(Now.AddMinutes(-10), "j.kowalski", "KS-101-01", "prac-101"),
            Event(Now.AddMinutes(-20), "a.nowak", "KS-101-01", "prac-101"),
            Event(Now.AddMinutes(-30), "j.kowalski", "KS-205-01", "prac-205")
        };

        var buffer = Buffer(clock, events, Stations(3, unavailable: 0));
        await buffer.IngestAsync();

        var result = await buffer.QueryAsync(new LoginQueryRequest(
            ComputerName: "KS-101-01",
            UserSamAccountName: "j.kowalski",
            FromUtc: Now.AddMinutes(-15),
            ToUtc: Now,
            ClassroomIds: new[] { "prac-101" },
            Take: 10));

        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalMatched);
        Assert.Equal("KS-101-01", result.Items[0].ComputerName);
    }

    [Fact]
    public async Task Powtarzajace_sie_odczyty_nie_dubluja_zdarzen()
    {
        var clock = new FakeClock(Now);
        var loginEvent = Event(Now.AddMinutes(-1), "j.kowalski", "KS-101-01", "prac-101");
        var source = new FakeSource(new[] { loginEvent }, Stations(2, unavailable: 0));
        var buffer = new LoginHistoryBuffer(source, new LoginHistoryOptions(), clock);

        var first = await buffer.IngestAsync();
        var second = await buffer.IngestAsync();

        Assert.Single(first);
        Assert.Empty(second);
        Assert.Equal(1, buffer.Count);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public void Kolektor_gwarantuje_odczyt_nie_rzadziej_niz_co_2_minuty()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), LoginCollectorService.ClampInterval(60));
        Assert.Equal(TimeSpan.FromSeconds(120), LoginCollectorService.ClampInterval(9999));
        Assert.Equal(TimeSpan.FromSeconds(10), LoginCollectorService.ClampInterval(1));
        Assert.True(LoginCollectorService.ClampInterval(300) <= TimeSpan.FromMinutes(2));
    }

    private static LoginHistoryBuffer Buffer(
        FakeClock clock,
        IReadOnlyList<LoginEventDto> events,
        IReadOnlyList<StationLogStatus> stations) =>
        new(new FakeSource(events, stations), new LoginHistoryOptions { RetentionDays = 30 }, clock);

    private static LoginEventDto Event(
        DateTimeOffset occurredAt,
        string user,
        string computer,
        string classroomId) =>
        MockLoginEventSource.Create(
            user, null, computer, occurredAt,
            LoginEventType.Logon, "Security log (4624)", classroomId);

    private static IReadOnlyList<StationLogStatus> Stations(int count, int unavailable)
    {
        var stations = new List<StationLogStatus>();
        for (var index = 1; index <= count; index++)
        {
            var isUnavailable = index > count - unavailable;
            stations.Add(new StationLogStatus(
                $"KS-101-{index:00}",
                !isUnavailable,
                isUnavailable ? "Brak uprawnień do dziennika (Event Log Readers)." : null));
        }

        return stations;
    }

    private sealed class FakeSource : ILoginEventSource
    {
        private readonly LoginIngestBatch _batch;

        public FakeSource(IReadOnlyList<LoginEventDto> events, IReadOnlyList<StationLogStatus> stations)
        {
            _batch = new LoginIngestBatch(events, stations);
        }

        public int Calls { get; private set; }

        public Task<LoginIngestBatch> ReadSinceAsync(
            DateTimeOffset sinceUtc,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_batch);
        }
    }
}
