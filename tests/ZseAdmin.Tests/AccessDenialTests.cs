using ZseAdmin.Client.Core.Connection;
using ZseAdmin.Client.Core.Display;
using ZseAdmin.Client.Core.ViewModels;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using Xunit;

namespace ZseAdmin.Tests;

/// <summary>
/// ZAD-06 — stan „Brak dostępu” (D10) musi być odróżnialny od braku danych:
/// <c>unknown</c> ≠ <c>offline</c>, a <c>unavailable</c> ≠ „brak dostępu”.
/// </summary>
public sealed class AccessDenialTests
{
    [Fact]
    public void Komunikat_braku_dostepu_nie_jest_komunikatem_braku_danych()
    {
        Assert.DoesNotContain("brak danych", AccessDenial.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(DisplayFormat.Completeness(DataCompleteness.Unavailable), AccessDenial.Message);
        Assert.NotEqual(DisplayFormat.Status(ComputerStatus.Unknown), AccessDenial.Message);
        Assert.Contains("Brak dostępu", AccessDenial.Message, StringComparison.Ordinal);
        Assert.Contains("administrator", AccessDenial.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Welcome_bez_zadnego_uprawnienia_to_brak_dostepu()
    {
        Assert.True(AccessDenial.HasNoCapabilities(Array.Empty<string>()));
        Assert.False(AccessDenial.HasNoCapabilities(new[] { "logins.read" }));
    }

    [Theory]
    [InlineData("The server returned status code '401' when status code '101' was expected.", true)]
    [InlineData("Unauthorized", true)]
    [InlineData("The operation timed out.", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Odmowa_401_jest_rozpoznawana_jako_brak_dostepu(string? transportError, bool expected)
    {
        Assert.Equal(expected, AccessDenial.IsAuthenticationFailure(transportError));
    }

    [Fact]
    public void Tylko_forbidden_jest_brakiem_dostepu_a_inne_bledy_nie()
    {
        Assert.True(AccessDenial.IsForbidden(
            new ServerErrorException(ErrorCode.Forbidden, "Brak uprawnienia.")));
        Assert.False(AccessDenial.IsForbidden(
            new ServerErrorException(ErrorCode.UpstreamUnavailable, "AD nieosiągalne.", "LDAP")));
        Assert.False(AccessDenial.IsForbidden(
            new ServerErrorException(ErrorCode.NotFound, "Nie ma konta.")));
        Assert.False(AccessDenial.IsForbidden(new TimeoutException("timeout")));
    }
}
