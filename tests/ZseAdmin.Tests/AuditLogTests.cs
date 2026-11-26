using Microsoft.Extensions.Logging.Abstractions;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Audit;
using Xunit;

namespace ZseAdmin.Tests;

public sealed class AuditLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Wpis_audytu_zawiera_wszystkie_wymagane_pola_i_jest_filtrowalny_po_celu()
    {
        var clock = new FakeClock(Now);
        var audit = new AuditLog(clock, new AuditOptions(), NullLogger<AuditLog>.Instance);

        await audit.RecordAsync(Record("KS-101-01", "success"));
        await audit.RecordAsync(Record("j.kowalski", "failed"));

        var result = await audit.QueryAsync("KS-101", 50);

        Assert.Equal(1, result.TotalMatched);
        var entry = Assert.Single(result.Items);
        Assert.Equal("admin.it", entry.Actor);
        Assert.Equal("computers.control", entry.Capability);
        Assert.Equal("KS-101-01", entry.Target);
        Assert.Equal("przed", entry.Before);
        Assert.Equal("po", entry.After);
        Assert.Equal("success", entry.Result);
        Assert.Equal("10.20.0.5", entry.RemoteAddress);
    }

    [Fact]
    public async Task Wpisy_starsze_niz_12_miesiecy_sa_usuwane()
    {
        var clock = new FakeClock(Now);
        var audit = new AuditLog(clock, new AuditOptions { RetentionMonths = 12 }, NullLogger<AuditLog>.Instance);

        await audit.RecordAsync(Record("stary", "success"));
        clock.Advance(TimeSpan.FromDays(400));
        await audit.RecordAsync(Record("nowy", "success"));

        var result = await audit.QueryAsync(null, 50);

        Assert.Equal(1, result.TotalMatched);
        Assert.Equal("nowy", result.Items[0].Target);
    }

    private static AuditRecord Record(string target, string result) => new(
        "admin.it",
        "Administrator",
        "computers.control",
        "RestartComputer",
        target,
        "przed",
        "po",
        result,
        "10.20.0.5",
        "test");
}
