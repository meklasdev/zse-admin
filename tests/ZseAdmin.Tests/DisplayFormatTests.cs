using ZseAdmin.Client.Core.Display;
using ZseAdmin.Contracts;
using Xunit;

namespace ZseAdmin.Tests;

public sealed class DisplayFormatTests
{
    [Theory]
    [InlineData(1, "1 komputer")]
    [InlineData(3, "3 komputery")]
    [InlineData(5, "5 komputerów")]
    [InlineData(12, "12 komputerów")]
    [InlineData(22, "22 komputery")]
    [InlineData(0, "0 komputerów")]
    public void Odmiana_liczby_komputerow_jest_poprawna(int value, string expected)
    {
        Assert.Equal(expected, DisplayFormat.Computers(value));
    }

    [Fact]
    public void Brak_daty_to_myslnik_a_nie_puste_pole()
    {
        Assert.Equal(DisplayFormat.Missing, DisplayFormat.DateTime(null));
    }

    [Fact]
    public void Stan_unknown_nie_jest_opisywany_jako_offline()
    {
        Assert.Equal("brak danych", DisplayFormat.Status(ComputerStatus.Unknown));
        Assert.Equal("offline", DisplayFormat.Status(ComputerStatus.Offline));
        Assert.Equal("online", DisplayFormat.Status(ComputerStatus.Online));
    }

    [Fact]
    public void Kompletnosc_ma_polskie_etykiety()
    {
        Assert.Equal("kompletne", DisplayFormat.Completeness(DataCompleteness.Complete));
        Assert.Equal("częściowe", DisplayFormat.Completeness(DataCompleteness.Partial));
        Assert.Equal("niedostępne", DisplayFormat.Completeness(DataCompleteness.Unavailable));
    }
}
