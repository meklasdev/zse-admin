using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Security;
using Xunit;

namespace ZseAdmin.Tests;

/// <summary>
/// Bramka produkcyjna (M3): zła konfiguracja bezpieczeństwa = serwer nie startuje.
/// Te reguły implementują kryterium akceptacji „tokeny z appsettings.json są zabronione,
/// a połączenie nie-wss jest odrzucane”.
/// </summary>
public sealed class SecurityConfigurationValidatorTests
{
    private static ZseAdminOptions MocklessAdOptions() =>
        new()
        {
            UseMockData = false,
            Directory = new DirectoryOptions { UseSsl = true }
        };

    private static SecurityOptions ProductionSecurity() =>
        new()
        {
            Authentication = new AuthenticationOptions { Mode = AuthenticationModes.ActiveDirectory },
            RequireSecureTransport = true,
            Tokens = new List<TokenEntry>()
        };

    [Fact]
    public void Poprawna_konfiguracja_produkcyjna_przechodzi()
    {
        var errors = SecurityConfigurationValidator.Validate(true, MocklessAdOptions(), ProductionSecurity());

        Assert.Empty(errors);
    }

    [Fact]
    public void Token_w_konfiguracji_produkcyjnej_blokuje_start()
    {
        var security = ProductionSecurity();
        security.Tokens.Add(new TokenEntry
        {
            Name = "administrator",
            Token = "tajny-token",
            Role = Roles.Administrator
        });

        var errors = SecurityConfigurationValidator.Validate(true, MocklessAdOptions(), security);

        Assert.Contains(errors, e => e.Contains("Tokens", StringComparison.Ordinal));
    }

    [Fact]
    public void Tryb_tokenow_na_produkcji_blokuje_start()
    {
        var security = ProductionSecurity();
        security.Authentication.Mode = AuthenticationModes.DevelopmentTokens;

        var errors = SecurityConfigurationValidator.Validate(true, MocklessAdOptions(), security);

        Assert.Contains(errors, e => e.Contains("ActiveDirectory", StringComparison.Ordinal));
    }

    [Fact]
    public void Brak_wss_na_produkcji_blokuje_start()
    {
        var security = ProductionSecurity();
        security.RequireSecureTransport = false;

        var errors = SecurityConfigurationValidator.Validate(true, MocklessAdOptions(), security);

        Assert.Contains(errors, e => e.Contains("wss", StringComparison.Ordinal));
    }

    [Fact]
    public void Dane_przykladowe_na_produkcji_blokowane_sa()
    {
        var options = new ZseAdminOptions { UseMockData = true };

        var errors = SecurityConfigurationValidator.Validate(true, options, ProductionSecurity());

        Assert.Contains(errors, e => e.Contains("UseMockData", StringComparison.Ordinal));
    }

    [Fact]
    public void Ldap_bez_tls_na_produkcji_blokuje_start()
    {
        var options = new ZseAdminOptions
        {
            UseMockData = false,
            Directory = new DirectoryOptions { UseSsl = false }
        };

        var errors = SecurityConfigurationValidator.Validate(true, options, ProductionSecurity());

        Assert.Contains(errors, e => e.Contains("UseSsl", StringComparison.Ordinal));
    }

    [Fact]
    public void Development_nie_podlega_bramce()
    {
        // W development wszystko wolno — bramka dotyczy wyłącznie produkcji.
        var errors = SecurityConfigurationValidator.Validate(false, new ZseAdminOptions(), new SecurityOptions());

        Assert.Empty(errors);
    }
}
