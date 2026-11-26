using Microsoft.Extensions.Logging.Abstractions;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Audit;
using ZseAdmin.Server.Infrastructure.Mock;
using ZseAdmin.Server.Realtime;
using ZseAdmin.Server.Security;
using ZseAdmin.Server.Services;

namespace ZseAdmin.Tests;

/// <summary>
/// Komplet zależności <see cref="MessageRouter"/> po M2/M3/M4. Testy mogą podmienić
/// jedno ogniwo (źródło logowań, politykę, wykonawcę akcji), reszta pozostaje realna.
/// </summary>
internal static class TestRouterFactory
{
    public static MessageRouter Create(
        FakeClock clock,
        ILoginLog? logins = null,
        IAdDirectory? users = null,
        IComputerDirectory? computers = null,
        SecurityOptions? security = null,
        IComputerControl? computerControl = null,
        IAdWriteService? adWrite = null,
        IAuditLog? audit = null,
        ILoginAccessPolicy? loginAccess = null,
        ActionOptions? actions = null)
    {
        security ??= new SecurityOptions();
        actions ??= new ActionOptions();

        var classrooms = new MockClassroomDirectory(clock);
        computers ??= new MockComputerDirectory(clock);
        users ??= new MockAdDirectory(clock);
        logins ??= new MockLoginLog(clock);
        audit ??= new AuditLog(clock, new AuditOptions(), NullLogger<AuditLog>.Instance);

        loginAccess ??= new LoginAccessPolicy(
            new ConfiguredTeacherClassroomAccess(security), computers, clock, security);

        computerControl ??= new MockComputerControl(computers, actions);
        adWrite ??= new MockAdWriteService(users, clock);

        var confirmations = new ConfirmationStore(clock, actions);
        var adminActions = new AdminActionService(
            computerControl, adWrite, audit, confirmations, clock, security, actions);

        return new MessageRouter(
            classrooms,
            computers,
            logins,
            users,
            new FreshnessService(clock),
            new ZseAdminOptions(),
            clock,
            loginAccess,
            audit,
            adminActions);
    }

    public static AuditLog CreateAudit(FakeClock clock) =>
        new(clock, new AuditOptions(), NullLogger<AuditLog>.Instance);
}
