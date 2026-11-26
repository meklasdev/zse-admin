using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Services;
using Xunit;

namespace ZseAdmin.Tests;

public sealed class FreshnessServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Swieze_dane_nie_sa_oznaczone_jako_stale()
    {
        var clock = new FakeClock(Now);
        var service = new FreshnessService(clock);

        var result = service.Create("test", QueryMetadata.Complete(Now.AddSeconds(-30)), TimeSpan.FromMinutes(2));

        Assert.False(result.IsStale);
        Assert.Equal(DataCompleteness.Complete, result.Completeness);
        Assert.Equal(Now.AddSeconds(-30), result.ObservedAtUtc);
    }

    [Fact]
    public void Stare_dane_sa_oznaczone_jako_stale_z_powodem()
    {
        var clock = new FakeClock(Now);
        var service = new FreshnessService(clock);

        var result = service.Create("test", QueryMetadata.Complete(Now.AddMinutes(-10)), TimeSpan.FromMinutes(2));

        Assert.True(result.IsStale);
        Assert.NotNull(result.Note);
        Assert.Contains("starsze", result.Note);
    }

    [Fact]
    public void Brak_obserwacji_to_unavailable_a_nie_offline()
    {
        var clock = new FakeClock(Now);
        var service = new FreshnessService(clock);

        var result = service.Create("test", QueryMetadata.Unavailable("Brak dostępu do dziennika."), TimeSpan.FromMinutes(2));

        Assert.True(result.IsStale);
        Assert.Null(result.ObservedAtUtc);
        Assert.Equal(DataCompleteness.Unavailable, result.Completeness);
        Assert.Equal("Brak dostępu do dziennika.", result.Note);
    }

    [Fact]
    public void Czastkowe_dane_zachowuja_kompletnosc()
    {
        var clock = new FakeClock(Now);
        var service = new FreshnessService(clock);
        var metadata = new QueryMetadata(Now.AddSeconds(-10), DataCompleteness.Partial, "2 z 6 stacji bez dostępu.");

        var result = service.Create("test", metadata, TimeSpan.FromMinutes(2));

        Assert.Equal(DataCompleteness.Partial, result.Completeness);
        Assert.False(result.IsStale);
        Assert.Equal("2 z 6 stacji bez dostępu.", result.Note);
    }

    [Fact]
    public void Nieaktualne_dane_nie_udaja_kompletnych()
    {
        var clock = new FakeClock(Now);
        var service = new FreshnessService(clock);

        var result = service.Create("test", QueryMetadata.Complete(Now.AddHours(-1)), TimeSpan.FromMinutes(2));

        Assert.True(result.IsStale);
        Assert.Equal(DataCompleteness.Partial, result.Completeness);
        Assert.NotNull(result.Note);
    }
}
