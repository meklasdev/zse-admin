using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Logins;
using ZseAdmin.Server.Infrastructure.Mock;
using ZseAdmin.Server.Realtime;
using ZseAdmin.Server.Security;
using Xunit;

namespace ZseAdmin.Tests;

/// <summary>
/// Kryterium M3: nauczyciel proszący o historię z pracowni, w której nie ma zajęć,
/// dostaje <c>forbidden</c>; zakres jest egzekwowany serwerowo (także dla push).
/// </summary>
public sealed class TeacherScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Nauczyciel_bez_zajec_w_pracowni_dostaje_forbidden()
    {
        var router = TestRouterFactory.Create(new FakeClock(Now), security: ScopeSecurity());

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(MessageTypes.LoginsQuery, "r-1", new LoginQueryRequest(ClassroomId: "prac-205")),
            Teacher()));

        Assert.Equal(ErrorCode.Forbidden, exception.Code);
        Assert.Contains("zajęć", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nauczyciel_widzi_tylko_logowania_ze_swojej_pracowni()
    {
        var clock = new FakeClock(Now);
        var buffer = BufferWithTwoClassrooms(clock);
        var router = TestRouterFactory.Create(clock, security: ScopeSecurity(), logins: buffer);

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.LoginsQuery, "r-2", new LoginQueryRequest(Take: 50)),
            Teacher());

        var payload = response.ReadPayload<LoginsResponse>();

        Assert.NotNull(payload);
        Assert.Equal(1, payload!.TotalMatched);
        Assert.All(payload.Items, e => Assert.Equal("prac-101", e.ClassroomId));
    }

    [Fact]
    public async Task Administrator_nie_jest_ograniczany_zakresem_pracowni()
    {
        var clock = new FakeClock(Now);
        var buffer = BufferWithTwoClassrooms(clock);
        var router = TestRouterFactory.Create(clock, security: ScopeSecurity(), logins: buffer);

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.LoginsQuery, "r-3", new LoginQueryRequest(Take: 50)),
            RoleCatalog.CreateIdentity("admin.it", Roles.Administrator));

        var payload = response.ReadPayload<LoginsResponse>();

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.TotalMatched);
    }

    [Fact]
    public async Task Filtr_po_komputerze_z_innej_pracowni_jest_odrzucony()
    {
        var router = TestRouterFactory.Create(new FakeClock(Now), security: ScopeSecurity());

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.LoginsQuery,
                "r-4",
                new LoginQueryRequest(ComputerName: "KS-205-01")),
            Teacher()));

        Assert.Equal(ErrorCode.Forbidden, exception.Code);
    }

    [Fact]
    public void Welcome_niesie_zakres_pracowni_a_push_jest_filtrowany()
    {
        var clock = new FakeClock(Now);
        var security = ScopeSecurity();
        var router = TestRouterFactory.Create(clock, security: security);
        var policy = new LoginAccessPolicy(
            new ConfiguredTeacherClassroomAccess(security),
            new MockComputerDirectory(clock),
            clock,
            security);

        var welcome = router.BuildWelcome(Teacher(), "sesja-1");
        Assert.NotNull(welcome.ClassroomScope);
        Assert.Equal(new[] { "prac-101" }, welcome.ClassroomScope);

        var other = MockLoginEventSource.Create(
            "j.kowalski", null, "KS-205-01", Now,
            LoginEventType.Logon, "Security log (4624)", "prac-205");

        Assert.False(policy.CanSeeEvent(Teacher(), other));
        Assert.True(policy.CanSeeEvent(RoleCatalog.CreateIdentity("admin.it", Roles.Administrator), other));
    }

    [Fact]
    public void Okno_zajec_z_planu_lekcji_aktywuje_pracownie()
    {
        var clock = new FakeClock(Now);
        var security = new SecurityOptions
        {
            TeachingSlots = new List<TeachingSlotOptions>
            {
                new()
                {
                    Teacher = "k.mazur",
                    ClassroomId = "prac-205",
                    Days = new List<string> { clock.UtcNow.DayOfWeek.ToString() },
                    StartUtc = "00:00",
                    EndUtc = "23:59"
                }
            }
        };

        var access = new ConfiguredTeacherClassroomAccess(security);

        Assert.True(access.HasLessonIn("ZSE\\k.mazur", "prac-205", clock.UtcNow));
        Assert.False(access.HasLessonIn("k.mazur", "prac-101", clock.UtcNow));
    }

    private static SecurityOptions ScopeSecurity() => new()
    {
        EnforceTeacherClassroomScope = true,
        TeacherClassrooms = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["k.mazur"] = new List<string> { "prac-101" }
        }
    };

    private static ClientIdentity Teacher() => RoleCatalog.CreateIdentity("k.mazur", Roles.Teacher);

    private static LoginHistoryBuffer BufferWithTwoClassrooms(FakeClock clock)
    {
        var events = new[]
        {
            MockLoginEventSource.Create(
                "j.kowalski", null, "KS-101-01", Now.AddMinutes(-5),
                LoginEventType.Logon, "Security log (4624)", "prac-101"),
            MockLoginEventSource.Create(
                "a.nowak", null, "KS-205-01", Now.AddMinutes(-10),
                LoginEventType.Logon, "Security log (4624)", "prac-205")
        };

        var stations = new[]
        {
            new StationLogStatus("KS-101-01", true, null),
            new StationLogStatus("KS-205-01", true, null)
        };

        var buffer = new LoginHistoryBuffer(
            new FixedSource(events, stations), new LoginHistoryOptions(), clock);
        buffer.IngestAsync().GetAwaiter().GetResult();
        return buffer;
    }

    private sealed class FixedSource : ILoginEventSource
    {
        private readonly LoginIngestBatch _batch;

        public FixedSource(
            IReadOnlyList<LoginEventDto> events,
            IReadOnlyList<StationLogStatus> stations)
        {
            _batch = new LoginIngestBatch(events, stations);
        }

        public Task<LoginIngestBatch> ReadSinceAsync(
            DateTimeOffset sinceUtc,
            CancellationToken cancellationToken = default) => Task.FromResult(_batch);
    }
}
