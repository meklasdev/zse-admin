using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Infrastructure.Actions;
using ZseAdmin.Server.Infrastructure.Classrooms;
using ZseAdmin.Server.Infrastructure.Mock;
using ZseAdmin.Server.Realtime;
using ZseAdmin.Server.Security;
using ZseAdmin.Server.Services;
using Xunit;

namespace ZseAdmin.Tests;

public sealed class MessageRouterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ping_zwraca_czas_serwera()
    {
        var router = CreateRouter();

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.Ping, "req-1"),
            RoleCatalog.CreateIdentity("t", Roles.ReadOnly));

        Assert.Equal(MessageTypes.Ping, response.Type);
        Assert.Equal("req-1", response.RequestId);
        Assert.Equal(Now, response.ReadPayload<PongResponse>()!.ServerTimeUtc);
    }

    [Fact]
    public async Task Nieznany_typ_wiadomosci_jest_bledem_invalid_request()
    {
        var router = CreateRouter();

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create("cokolwiek.innego", "req-2"),
            RoleCatalog.CreateIdentity("t", Roles.Administrator)));

        Assert.Equal(ErrorCode.InvalidRequest, exception.Code);
    }

    [Fact]
    public async Task Rola_readonly_nie_moze_pobrac_konta_AD()
    {
        var router = CreateRouter();

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(MessageTypes.UsersGet, "req-3", new UserLookupRequest("j.kowalski")),
            RoleCatalog.CreateIdentity("t", Roles.ReadOnly)));

        Assert.Equal(ErrorCode.Forbidden, exception.Code);
    }

    [Fact]
    public async Task Nauczyciel_moze_pobrac_konto_AD()
    {
        var router = CreateRouter();

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.UsersGet, "req-4", new UserLookupRequest("j.kowalski")),
            RoleCatalog.CreateIdentity("k.mazur", Roles.Teacher));

        var payload = response.ReadPayload<UserResponse>();

        Assert.NotNull(payload);
        Assert.Equal("j.kowalski", payload!.User.SamAccountName);
        Assert.False(payload.Freshness.IsStale);
        Assert.NotNull(payload.Freshness.ObservedAtUtc);
    }

    [Fact]
    public async Task Lista_komputerow_bez_offline_zwraca_tylko_online()
    {
        var router = CreateRouter();

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.ComputersList, "req-5", new ComputerListRequest(IncludeOffline: false)),
            RoleCatalog.CreateIdentity("t", Roles.ReadOnly));

        var payload = response.ReadPayload<ComputersResponse>();

        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Items);
        Assert.All(payload.Items, c => Assert.Equal(ComputerStatus.Online, c.Status));
    }

    [Fact]
    public async Task Logowania_dla_nieznanego_komputera_zwracaja_pusta_liste_bez_bledu()
    {
        var router = CreateRouter();

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.LoginsQuery, "req-6", new LoginQueryRequest(ComputerName: "KS-999-99")),
            RoleCatalog.CreateIdentity("t", Roles.ReadOnly));

        var payload = response.ReadPayload<LoginsResponse>();

        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalMatched);
        Assert.True(payload.Freshness.ObservedAtUtc is not null);
    }

    [Fact]
    public async Task Nieistniejace_konto_zwraca_not_found()
    {
        var router = CreateRouter();

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(MessageTypes.UsersGet, "req-7", new UserLookupRequest("nie.ma.takiego")),
            RoleCatalog.CreateIdentity("admin.it", Roles.Administrator)));

        Assert.Equal(ErrorCode.NotFound, exception.Code);
    }

    [Fact]
    public void Welcome_zwraca_role_i_uprawnienia_sesji()
    {
        var router = CreateRouter();
        var identity = RoleCatalog.CreateIdentity("k.mazur", Roles.Teacher);

        var welcome = router.BuildWelcome(identity, "sesja-1");

        Assert.Equal("sesja-1", welcome.SessionId);
        Assert.Equal(Roles.Teacher, welcome.Role);
        Assert.Contains(Capability.LoginsRead, welcome.Capabilities);
        Assert.DoesNotContain(Capability.UsersWrite, welcome.Capabilities);
        Assert.Equal(ProtocolJson.ProtocolVersion, welcome.ProtocolVersion);
    }

    [Fact]
    public async Task Zrodlo_niedostepne_daje_upstream_unavailable_a_nie_pusta_liste()
    {
        var router = CreateRouter(users: new UnavailableAdDirectory());

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(MessageTypes.UsersGet, "req-8", new UserLookupRequest("j.kowalski")),
            RoleCatalog.CreateIdentity("admin.it", Roles.Administrator)));

        Assert.Equal(ErrorCode.UpstreamUnavailable, exception.Code);
        Assert.Contains("Active Directory", exception.Message);
    }

    [Fact]
    public async Task Historia_niedostepna_ma_kompletnosc_unavailable_a_nie_pusta_liste()
    {
        var router = CreateRouter(logins: new UnavailableLoginLogForTests());

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.LoginsQuery, "req-9"),
            RoleCatalog.CreateIdentity("t", Roles.ReadOnly));

        var payload = response.ReadPayload<LoginsResponse>();

        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(DataCompleteness.Unavailable, payload.Freshness.Completeness);
        Assert.NotNull(payload.Freshness.Note);
    }

    [Fact]
    public async Task Zakres_przed_retencja_jest_jawnie_oznaczony()
    {
        var router = CreateRouter();

        var response = await router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.LoginsQuery,
                "req-10",
                new LoginQueryRequest(FromUtc: Now.AddDays(-60))),
            RoleCatalog.CreateIdentity("t", Roles.ReadOnly));

        var payload = response.ReadPayload<LoginsResponse>();

        Assert.NotNull(payload);
        Assert.Equal(DataCompleteness.Partial, payload!.Freshness.Completeness);
        Assert.Contains("retencj", payload.Freshness.Note, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Nauczyciel_bez_przypisanej_pracowni_nie_widzi_historii_logowan()
    {
        var router = CreateRouter();

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(MessageTypes.LoginsQuery, "req-h1"),
            RoleCatalog.CreateIdentity("nieznany.nauczyciel", Roles.Teacher)));

        Assert.Equal(ErrorCode.Forbidden, exception.Code);
    }

    [Fact]
    public async Task Nauczyciel_nie_moze_pytac_o_pracownie_spoza_swojego_przypisania()
    {
        var router = CreateRouter();

        // k.mazur ma przypisane pracownie 101 i 102 (classrooms.json/ClassroomMap.Development).
        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(MessageTypes.LoginsQuery, "req-h2", new LoginQueryRequest(ClassroomId: "prac-205")),
            RoleCatalog.CreateIdentity("k.mazur", Roles.Teacher)));

        Assert.Equal(ErrorCode.Forbidden, exception.Code);
    }

    [Fact]
    public async Task Nauczyciel_widzi_historiae_swojej_pracowni()
    {
        var router = CreateRouter();

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.LoginsQuery, "req-h3", new LoginQueryRequest(ClassroomId: "prac-101")),
            RoleCatalog.CreateIdentity("k.mazur", Roles.Teacher));

        var payload = response.ReadPayload<LoginsResponse>();

        Assert.NotNull(payload);
        Assert.All(payload!.Items, e => Assert.Equal("prac-101", e.ClassroomId));
    }

    [Fact]
    public async Task Readonly_nie_moze_wykonac_akcji_na_stacji()
    {
        var router = CreateRouter();

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(MessageTypes.ComputersAction, "req-a1",
                new ComputerActionRequest("KS-101-01", ComputerAction.Restart, Confirm: true)),
            RoleCatalog.CreateIdentity("t", Roles.ReadOnly)));

        Assert.Equal(ErrorCode.Forbidden, exception.Code);
    }

    [Fact]
    public async Task Akcja_na_stacji_wymaga_potwierdzenia_confirm()
    {
        var router = CreateRouter();

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(MessageTypes.ComputersAction, "req-a2",
                new ComputerActionRequest("KS-101-01", ComputerAction.Restart, Confirm: false)),
            RoleCatalog.CreateIdentity("admin.it", Roles.Administrator)));

        Assert.Equal(ErrorCode.InvalidRequest, exception.Code);
    }

    [Fact]
    public async Task Administrator_restartuje_stacje_i_tworzy_wpis_audytu()
    {
        var auditLog = new InMemoryAuditLog();
        var router = CreateRouter(auditLog: auditLog);

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.ComputersAction, "req-a3",
                new ComputerActionRequest("KS-101-01", ComputerAction.Restart, Confirm: true)),
            RoleCatalog.CreateIdentity("admin.it", Roles.Administrator));

        var payload = response.ReadPayload<ComputerActionResponse>();

        Assert.NotNull(payload);
        Assert.True(payload!.Accepted);
        var entry = Assert.Single(auditLog.Entries);
        Assert.Equal("admin.it", entry.Actor);
        Assert.Equal("KS-101-01", entry.Target);
        Assert.Equal(Capability.ComputersControl, entry.Capability);
        Assert.Equal("accepted", entry.Result);
    }

    [Fact]
    public async Task Reset_hasla_wymaga_confirm_i_zapisuje_audyt_bez_hasla()
    {
        var auditLog = new InMemoryAuditLog();
        var router = CreateRouter(auditLog: auditLog);

        var rejected = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(MessageTypes.UsersResetPassword, "req-p1",
                new ResetPasswordRequest("j.kowalski", "nowehaslo123", Confirm: false)),
            RoleCatalog.CreateIdentity("admin.it", Roles.Administrator)));
        Assert.Equal(ErrorCode.InvalidRequest, rejected.Code);

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.UsersResetPassword, "req-p2",
                new ResetPasswordRequest("j.kowalski", "nowehaslo123", Confirm: true)),
            RoleCatalog.CreateIdentity("admin.it", Roles.Administrator));

        var payload = response.ReadPayload<ResetPasswordResponse>();

        Assert.NotNull(payload);
        Assert.True(payload!.Changed);
        var entry = Assert.Single(auditLog.Entries);
        Assert.Equal("j.kowalski", entry.Target);
        Assert.DoesNotContain("nowehaslo123", entry.Before ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("nowehaslo123", entry.After ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dodanie_do_grupy_zapisuje_wpis_audytu_ze_zmiana()
    {
        var auditLog = new InMemoryAuditLog();
        var router = CreateRouter(auditLog: auditLog);

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.UsersAddToGroup, "req-g1",
                new AddToGroupRequest("j.kowalski", "Klasa-3TI", Confirm: true)),
            RoleCatalog.CreateIdentity("admin.it", Roles.Administrator));

        var payload = response.ReadPayload<AddToGroupResponse>();

        Assert.NotNull(payload);
        Assert.True(payload!.Changed);
        var entry = Assert.Single(auditLog.Entries);
        Assert.Equal("j.kowalski → Klasa-3TI", entry.Target);
        Assert.Equal("ok", entry.Result);
    }

    [Fact]
    public async Task Audyt_dla_readonly_jest_forbidden()
    {
        var router = CreateRouter();

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(MessageTypes.AuditQuery, "req-au1", new AuditQueryRequest()),
            RoleCatalog.CreateIdentity("t", Roles.ReadOnly)));

        Assert.Equal(ErrorCode.Forbidden, exception.Code);
    }

    [Fact]
    public async Task Audyt_zwroci_wpisy_administratorowi()
    {
        var auditLog = new InMemoryAuditLog();
        await auditLog.RecordAsync(new AuditEntry(
            Guid.NewGuid(), Now, "admin.it", Roles.Administrator,
            Capability.ComputersControl, "KS-101-01",
            "status=online", "Wydano: restart", "accepted", "127.0.0.1"));
        var router = CreateRouter(auditLog: auditLog);

        var response = await router.HandleAsync(
            WsEnvelope.Create(MessageTypes.AuditQuery, "req-au2", new AuditQueryRequest()),
            RoleCatalog.CreateIdentity("admin.it", Roles.Administrator));

        var payload = response.ReadPayload<AuditResponse>();

        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal("KS-101-01", payload.Items[0].Target);
    }

    private sealed class InMemoryAuditLog : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = new();

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> QueryAsync(
            string? target,
            string? capability,
            int take,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<AuditEntry> query = Entries;
            if (!string.IsNullOrWhiteSpace(target))
            {
                query = query.Where(e => e.Target.Contains(target, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(capability))
            {
                query = query.Where(e => string.Equals(e.Capability, capability, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<AuditEntry>>(query.OrderByDescending(e => e.TimestampUtc).Take(take).ToList());
        }

        public Task<int> PruneAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
        {
            var removed = Entries.RemoveAll(e => e.TimestampUtc < cutoffUtc);
            return Task.FromResult(removed);
        }
    }

    private static MessageRouter CreateRouter(
        IAdDirectory? users = null,
        ILoginLog? logins = null,
        IAuditLog? auditLog = null,
        IComputerActionExecutor? computerActions = null,
        IAdAccountWriter? accountWriter = null)
    {
        var clock = new FakeClock(Now);
        var options = new ZseAdminOptions();

        return new MessageRouter(
            new MockClassroomDirectory(clock),
            new MockComputerDirectory(clock),
            logins ?? new MockLoginLog(clock),
            users ?? new MockAdDirectory(clock),
            new FreshnessService(clock),
            options,
            clock,
            ClassroomMap.Development(),
            auditLog ?? new InMemoryAuditLog(),
            computerActions ?? new MockComputerActionExecutor(),
            accountWriter ?? new MockAdAccountWriter());
    }

    private sealed class UnavailableAdDirectory : IAdDirectory
    {
        public Task<AdUserQueryResult> FindUserAsync(string samAccountName, CancellationToken cancellationToken = default) =>
            throw new SourceUnavailableException("Active Directory", "Nie można połączyć się z DC.");
    }

    private sealed class UnavailableLoginLogForTests : ILoginLog
    {
        public DateTimeOffset? RetentionStartUtc => null;

        public Task<LoginQueryResult> QueryAsync(
            LoginQueryRequest query,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<string>? allowedClassroomIds = null) =>
            Task.FromResult(new LoginQueryResult(
                Array.Empty<LoginEventDto>(),
                0,
                QueryMetadata.Unavailable("Historia logowań nie jest jeszcze podłączona (etap M2).")));
    }
}
