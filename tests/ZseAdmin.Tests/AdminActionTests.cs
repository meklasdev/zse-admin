using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Mock;
using ZseAdmin.Server.Infrastructure.Windows;
using ZseAdmin.Server.Realtime;
using ZseAdmin.Server.Security;
using Xunit;

namespace ZseAdmin.Tests;

/// <summary>Kryteria akceptacji M4: uprawnienia, błąd źródła, audyt, potwierdzenie.</summary>
public sealed class AdminActionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Rola_bez_computers_control_jest_odrzucona_serwerowo()
    {
        var router = TestRouterFactory.Create(new FakeClock(Now));

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.ActionPrepare,
                "r-1",
                new ActionPrepareRequest(AdminActionKind.RestartComputer, "KS-101-01")),
            RoleCatalog.CreateIdentity("t", Roles.ReadOnly)));

        Assert.Equal(ErrorCode.Forbidden, exception.Code);
        Assert.Contains("computers.control", exception.Message);
    }

    [Fact]
    public async Task Nauczyciel_nie_moze_zmieniac_AD()
    {
        var router = TestRouterFactory.Create(new FakeClock(Now));

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.ActionPrepare,
                "r-2",
                new ActionPrepareRequest(AdminActionKind.ResetPassword, "j.kowalski")),
            RoleCatalog.CreateIdentity("k.mazur", Roles.Teacher)));

        Assert.Equal(ErrorCode.Forbidden, exception.Code);
        Assert.Contains("users.write", exception.Message);
    }

    [Fact]
    public async Task Niepowodzenie_akcji_to_upstream_unavailable_z_nazwa_zrodla_i_wpisem_o_porazce()
    {
        var clock = new FakeClock(Now);
        var audit = TestRouterFactory.CreateAudit(clock);
        var router = TestRouterFactory.Create(
            clock, computerControl: new FailingComputerControl(), audit: audit);
        var admin = Admin();

        var prepared = await PrepareAsync(router, AdminActionKind.RestartComputer, "KS-101-01", admin);

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.ActionExecute,
                "r-4",
                new ActionExecuteRequest(prepared.ConfirmationToken, true)),
            admin));

        Assert.Equal(ErrorCode.UpstreamUnavailable, exception.Code);
        Assert.Contains("Stacja KS-101-01", exception.Message);
        Assert.Contains("Access denied", exception.Detail!);

        var auditResult = await audit.QueryAsync("KS-101-01", 50);
        Assert.Contains(auditResult.Items, e => e.Result == "failed");
    }

    [Fact]
    public async Task Kazda_zmiana_ma_wpis_audytowy_z_aktorem_celem_przed_i_po()
    {
        var clock = new FakeClock(Now);
        var audit = TestRouterFactory.CreateAudit(clock);
        var router = TestRouterFactory.Create(clock, audit: audit);
        var admin = Admin();

        var prepared = await PrepareAsync(
            router, AdminActionKind.AddToGroup, "j.kowalski", admin, groupName: "Pracownia101");

        var response = await router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.ActionExecute,
                "r-5",
                new ActionExecuteRequest(prepared.ConfirmationToken, true, "zajęcia w pracowni")),
            admin);

        var executed = response.ReadPayload<ActionExecuteResponse>();
        var auditResult = await audit.QueryAsync("j.kowalski", 50);

        Assert.NotNull(executed);
        Assert.True(executed!.Success);
        Assert.Single(auditResult.Items);

        var entry = auditResult.Items[0];
        Assert.Equal("admin.it", entry.Actor);
        Assert.Equal(Roles.Administrator, entry.Role);
        Assert.Equal(Capability.UsersWrite, entry.Capability);
        Assert.Equal("j.kowalski", entry.Target);
        Assert.Equal("success", entry.Result);
        Assert.NotNull(entry.Before);
        Assert.Contains("Pracownia101", entry.After!);
        Assert.Contains("zajęcia w pracowni", entry.Detail!);
        Assert.Equal(entry.Id.ToString(), executed.AuditId);
    }

    [Fact]
    public async Task Potwierdzenie_jest_jednorazowe_i_zwiazane_z_aktorem()
    {
        var clock = new FakeClock(Now);
        var router = TestRouterFactory.Create(clock);
        var admin = Admin();
        var prepared = await PrepareAsync(router, AdminActionKind.LockComputer, "KS-101-01", admin);

        var otherActor = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.ActionExecute,
                "r-6",
                new ActionExecuteRequest(prepared.ConfirmationToken, true)),
            RoleCatalog.CreateIdentity("ktoś.inmy", Roles.Administrator)));

        Assert.Equal(ErrorCode.Forbidden, otherActor.Code);

        var second = await PrepareAsync(router, AdminActionKind.LockComputer, "KS-101-01", admin);
        await router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.ActionExecute,
                "r-7",
                new ActionExecuteRequest(second.ConfirmationToken, true)),
            admin);

        var reused = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.ActionExecute,
                "r-8",
                new ActionExecuteRequest(second.ConfirmationToken, true)),
            admin));

        Assert.Equal(ErrorCode.InvalidRequest, reused.Code);
    }

    [Fact]
    public async Task Haslo_tymczasowe_nie_trafia_do_audytu()
    {
        var clock = new FakeClock(Now);
        var audit = TestRouterFactory.CreateAudit(clock);
        var router = TestRouterFactory.Create(clock, audit: audit);
        var admin = Admin();

        var prepared = await PrepareAsync(router, AdminActionKind.ResetPassword, "j.kowalski", admin);
        var response = await router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.ActionExecute,
                "r-9",
                new ActionExecuteRequest(prepared.ConfirmationToken, true)),
            admin);

        var executed = response.ReadPayload<ActionExecuteResponse>();
        Assert.NotNull(executed!.TemporaryPassword);

        var password = executed.TemporaryPassword!;
        var auditResult = await audit.QueryAsync("j.kowalski", 50);
        var entry = Assert.Single(auditResult.Items);

        Assert.DoesNotContain(password, entry.Before!);
        Assert.DoesNotContain(password, entry.After!);
        Assert.DoesNotContain(password, entry.Detail ?? string.Empty);
    }

    [Fact]
    public async Task Reset_hasla_wymaga_wss_w_trybie_produkcyjnym()
    {
        var clock = new FakeClock(Now);
        var security = new SecurityOptions { RequireSecureTransport = true };
        var router = TestRouterFactory.Create(clock, security: security);
        var insecureAdmin = RoleCatalog.CreateIdentity(
            "admin.it", Roles.Administrator, null, "10.20.0.5", isSecureTransport: false);

        var exception = await Assert.ThrowsAsync<ProtocolException>(() => router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.ActionPrepare,
                "r-10",
                new ActionPrepareRequest(AdminActionKind.ResetPassword, "j.kowalski")),
            insecureAdmin));

        Assert.Equal(ErrorCode.Forbidden, exception.Code);
        Assert.Contains("wss", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Niezerowy_kod_polecenia_systemowego_to_blad_zrodla_a_nie_sukces()
    {
        var clock = new FakeClock(Now);
        var runner = new FakeProcessRunner(new ProcessResult(5, string.Empty, "Access denied."));
        var control = new WindowsComputerControl(
            new MockComputerDirectory(clock), runner, new ActionOptions(), clock);

        var exception = await Assert.ThrowsAsync<ActionFailedException>(
            () => control.RestartAsync("KS-101-01"));

        Assert.Equal("Stacja KS-101-01", exception.SourceName);
        Assert.Contains("Access denied", exception.Message);
    }

    [Fact]
    public void Argumenty_restartu_uzywaja_nazwy_unc_i_opoznienia()
    {
        var arguments = WindowsComputerControl.BuildRestartArguments("KS-101-01", 30);

        Assert.Contains("\\\\KS-101-01", arguments);
        Assert.Contains("/t 30", arguments);
        Assert.Contains("/r", arguments);
        Assert.DoesNotContain(";", arguments);
        Assert.DoesNotContain("&", arguments);
    }

    private static ClientIdentity Admin() =>
        RoleCatalog.CreateIdentity("admin.it", Roles.Administrator, null, "10.20.0.5", true);

    private static async Task<ActionPrepareResponse> PrepareAsync(
        MessageRouter router,
        AdminActionKind kind,
        string target,
        ClientIdentity identity,
        string? groupName = null)
    {
        var response = await router.HandleAsync(
            WsEnvelope.Create(
                MessageTypes.ActionPrepare,
                Guid.NewGuid().ToString("n"),
                new ActionPrepareRequest(kind, target, groupName)),
            identity);

        return response.ReadPayload<ActionPrepareResponse>()!;
    }

    private sealed class FailingComputerControl : IComputerControl
    {
        public Task<string> DescribeAsync(string computerName, CancellationToken cancellationToken = default) =>
            Task.FromResult("stan: online; IP: 10.20.101.11");

        public Task<ActionChange> RestartAsync(string computerName, CancellationToken cancellationToken = default) =>
            throw new ActionFailedException(
                $"Stacja {computerName}", "WinRM odrzucił połączenie (Access denied).");

        public Task<ActionChange> LockAsync(string computerName, CancellationToken cancellationToken = default) =>
            throw new ActionFailedException($"Stacja {computerName}", "Brak sesji użytkownika.");
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public FakeProcessRunner(ProcessResult result)
        {
            _result = result;
        }

        public Task<ProcessResult> RunAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) => Task.FromResult(_result);
    }
}
