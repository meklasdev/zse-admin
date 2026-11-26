using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Infrastructure.Mock;
using Xunit;

namespace ZseAdmin.Tests;

public sealed class MockDirectoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Komputery_mozna_filtrowac_po_pracowni()
    {
        var directory = new MockComputerDirectory(new FakeClock(Now));

        var all = await directory.ListAsync(null);
        var prac101 = await directory.ListAsync("prac-101");

        Assert.Equal(6, prac101.Items.Count);
        Assert.True(all.Items.Count > prac101.Items.Count);
        Assert.All(prac101.Items, c => Assert.Equal("prac-101", c.ClassroomId));
    }

    [Fact]
    public async Task Komputer_poza_pracownia_jest_widoczny_w_calosci()
    {
        var directory = new MockComputerDirectory(new FakeClock(Now));

        var all = await directory.ListAsync(null);

        Assert.Contains(all.Items, c => c.Name == "KS-SERWER-01" && c.ClassroomId is null);
    }

    [Fact]
    public async Task Odswiezanie_stanu_zwraca_tylko_zmiany()
    {
        var directory = new MockComputerDirectory(new FakeClock(Now));

        var changed = await directory.RefreshStatusesAsync();

        Assert.NotNull(changed);
        Assert.All(changed, c => Assert.NotNull(c.StatusObservedAtUtc));
    }

    [Fact]
    public async Task Logowania_sa_sortowane_malejaco_i_obcinane()
    {
        var log = new MockLoginLog(new FakeClock(Now));

        var result = await log.QueryAsync(new LoginQueryRequest(Take: 10));

        Assert.Equal(10, result.Items.Count);
        Assert.True(result.TotalMatched >= 10);
        Assert.True(result.Items.SequenceEqual(result.Items.OrderByDescending(e => e.OccurredAtUtc)));
        Assert.Equal(DataCompleteness.Partial, result.Metadata.Completeness);
    }

    [Fact]
    public async Task Filtr_logowan_po_komputerze_dziala()
    {
        var log = new MockLoginLog(new FakeClock(Now));

        var result = await log.QueryAsync(new LoginQueryRequest(ComputerName: "KS-101-01", Take: 50));

        Assert.All(result.Items, e => Assert.Equal("KS-101-01", e.ComputerName));
    }

    [Fact]
    public async Task Konto_istniejace_i_nieistniejace()
    {
        var users = new MockAdDirectory(new FakeClock(Now));

        var found = await users.FindUserAsync("j.kowalski");
        var missing = await users.FindUserAsync("nie.ma.takiego");

        Assert.NotNull(found.User);
        Assert.Contains("Klasa-3TI", found.User!.Groups);
        Assert.Null(missing.User);
    }
}
