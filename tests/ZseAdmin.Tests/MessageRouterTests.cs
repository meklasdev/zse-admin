using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Abstractions;
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
    public void Kazdy_typ_zadania_z_protokolu_ma_przypisane_uprawnienie()
    {
        // Fail-close: nowy typ w MessageTypes bez wpisu w MessageCapabilities nie może
        // po cichu działać jako „dozwolony”.
        Assert.Empty(MessageCapabilities.UnmappedProtocolRequests);
        Assert.All(
            MessageTypes.ClientRequestTypes,
            type => Assert.True(MessageCapabilities.TryGetCapability(type, out _), type));
    }

    public static TheoryData<string, object?> ZadaniaKlientaBezPing() => new()
    {
        { MessageTypes.ClassroomsList, null },
        { MessageTypes.ComputersList, null },
        { MessageTypes.LoginsQuery, null },
        { MessageTypes.UsersGet, new UserLookupRequest("j.kowalski") },
        { MessageTypes.AuditQuery, null },
        { MessageTypes.ActionPrepare, new ActionPrepareRequest(AdminActionKind.RestartComputer, "KS-101-01", null, null) },
        { MessageTypes.TopologyGet, null },
        { MessageTypes.FilesList, null },
        { MessageTypes.FilesStat, null }
    };

    [Theory]
    [MemberData(nameof(ZadaniaKlientaBezPing))]
    public async Task Rola_student_dostaje_forbidden_bez_danych(string type, object? payload)
    {
        var router = CreateRouter();
        var student = RoleCatalog.CreateIdentity("u.czen", "Student");

        var exception = await Assert.ThrowsAsync<ProtocolException>(() =>
            router.HandleAsync(WsEnvelope.Create(type, "req-student", payload), student));

        Assert.Equal(ErrorCode.Forbidden, exception.Code);
        Assert.DoesNotContain("KS-101", exception.Message);
    }

    [Theory]
    [InlineData(MessageTypes.TopologyGet)]
    [InlineData(MessageTypes.FilesList)]
    [InlineData(MessageTypes.FilesStat)]
    public async Task Readonly_przechodzi_sprawdzenie_uprawnienia_dla_topologii_i_plikow(string type)
    {
        // D7/D4: ReadOnly ma topology.read i files.read. Typy są zarezerwowane do ZAD-04/ZAD-07,
        // więc dziś odpowiedź to invalidRequest — ale NIE forbidden (uprawnienie jest).
        var router = CreateRouter();
        var readOnly = RoleCatalog.CreateIdentity("dyzurka", Roles.ReadOnly);

        var exception = await Assert.ThrowsAsync<ProtocolException>(() =>
            router.HandleAsync(WsEnvelope.Create(type, "req-ro"), readOnly));

        Assert.Equal(ErrorCode.InvalidRequest, exception.Code);
    }

    private static MessageRouter CreateRouter(
        IAdDirectory? users = null,
        ILoginLog? logins = null) =>
        TestRouterFactory.Create(new FakeClock(Now), logins: logins, users: users);

    private sealed class UnavailableAdDirectory : IAdDirectory
    {
        public Task<AdUserQueryResult> FindUserAsync(string samAccountName, CancellationToken cancellationToken = default) =>
            throw new SourceUnavailableException("Active Directory", "Nie można połączyć się z DC.");
    }

    private sealed class UnavailableLoginLogForTests : ILoginLog
    {
        public DateTimeOffset? RetentionStartUtc => null;

        public Task<LoginQueryResult> QueryAsync(LoginQueryRequest query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LoginQueryResult(
                Array.Empty<LoginEventDto>(),
                0,
                QueryMetadata.Unavailable("Historia logowań nie jest jeszcze podłączona (etap M2).")));
    }
}
