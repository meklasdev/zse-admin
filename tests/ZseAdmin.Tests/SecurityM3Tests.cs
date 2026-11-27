using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Security;
using Xunit;

namespace ZseAdmin.Tests;

/// <summary>Kryteria akceptacji M3: grupy AD, wss, brak tokenów na produkcji.</summary>
public sealed class SecurityM3Tests
{
    [Fact]
    public void Uzytkownik_bez_grupy_ZSE_Admin_Nauczyciele_nie_moze_sie_polaczyc()
    {
        var authenticator = WindowsAuthenticator(new FakeGroupProvider());
        var context = Context("ZSE\\uczen");

        var result = authenticator.Authenticate(context.Request);

        Assert.False(result.Succeeded);
        Assert.Equal(RejectionCodes.RoleGroupMissing, result.RejectionCode);
    }

    [Fact]
    public void Uczen_w_grupie_wykluczajacej_jest_odrzucony_mimo_grupy_nauczycieli()
    {
        // D10 = A: deny overrides — pomyłkowe dodanie ucznia do ZSE-Admin-Nauczyciele
        // nie daje sesji, bo grupa wykluczająca jest sprawdzana pierwsza.
        var authenticator = WindowsAuthenticator(
            new FakeGroupProvider("ZSE\\ZSE-Uczniowie", "ZSE\\ZSE-Admin-Nauczyciele"));
        var context = Context("ZSE\\u.czen");

        var result = authenticator.Authenticate(context.Request);

        Assert.False(result.Succeeded);
        Assert.Null(result.Identity);
        Assert.Equal(RejectionCodes.DeniedGroup, result.RejectionCode);
    }

    [Fact]
    public void Administrator_nienalezacy_do_grupy_uczniowskiej_nadal_wchodzi()
    {
        var authenticator = WindowsAuthenticator(
            new FakeGroupProvider("ZSE-Admin-Administratorzy", "Domain Users"));

        var result = authenticator.Authenticate(Context("ZSE\\admin.it").Request);

        Assert.True(result.Succeeded);
        Assert.Equal(Roles.Administrator, result.Identity!.Role);
    }

    [Fact]
    public void Członek_grupy_nauczycieli_dostaje_role_Teacher_bez_praw_zapisu()
    {
        var authenticator = WindowsAuthenticator(
            new FakeGroupProvider("ZSE\\ZSE-Admin-Nauczyciele"));
        var context = Context("ZSE\\k.mazur");

        var identity = authenticator.Authenticate(context.Request).Identity;

        Assert.NotNull(identity);
        Assert.Equal(Roles.Teacher, identity!.Role);
        Assert.Equal("k.mazur", identity.Name);
        Assert.Contains("ZSE-Admin-Nauczyciele", identity.Groups);
        Assert.False(identity.Has(Capability.UsersWrite));
        Assert.False(identity.Has(Capability.ComputersControl));
    }

    [Fact]
    public void Administratorzy_dostaja_role_Administrator()
    {
        var authenticator = WindowsAuthenticator(
            new FakeGroupProvider("ZSE-Admin-Administratorzy"));

        var identity = authenticator.Authenticate(Context("ZSE\\admin.it").Request).Identity;

        Assert.NotNull(identity);
        Assert.Equal(Roles.Administrator, identity!.Role);
        Assert.Contains(Capability.AuditRead, identity.Capabilities);
    }

    [Fact]
    public void Nieznana_grupa_nie_daje_zadnej_roli()
    {
        var security = new SecurityOptions();

        Assert.Null(RoleCatalog.RoleFromGroups(new[] { "Uczniowie", "Klasa-3TI" }, security.RoleGroups));
    }

    [Fact]
    public void Produkcja_startuje_tylko_z_bezpieczna_konfiguracja()
    {
        var options = SafeProductionOptions();
        var security = new SecurityOptions
        {
            Mode = SecurityOptions.WindowsMode,
            RequireSecureTransport = true,
            Tokens = new List<TokenEntry>()
        };

        Assert.Empty(ProductionConfigurationGuard.Validate(options, security));

        security.Tokens.Add(new TokenEntry { Name = "dev", Token = "dev-admin-token", Role = Roles.Administrator });
        security.RequireSecureTransport = false;
        options.UseMockData = true;

        var errors = ProductionConfigurationGuard.Validate(options, security);

        Assert.Contains(errors, e => e.Contains("Tokens", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("wss", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("UseMockData", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Produkcja_wymaga_niepustej_grupy_wykluczajacej()
    {
        var options = SafeProductionOptions();
        var security = new SecurityOptions
        {
            Mode = SecurityOptions.WindowsMode,
            RequireSecureTransport = true,
            Tokens = new List<TokenEntry>(),
            DeniedGroups = new List<string>()
        };

        var errors = ProductionConfigurationGuard.Validate(options, security);

        Assert.Contains(errors, e => e.Contains("DeniedGroups", StringComparison.OrdinalIgnoreCase));

        security.DeniedGroups = new List<string> { "Domain Users" };
        var broadErrors = ProductionConfigurationGuard.Validate(options, security);

        Assert.Contains(broadErrors, e => e.Contains("Domain Users", StringComparison.Ordinal));
    }

    [Fact]
    public void Domyslna_konfiguracja_blokuje_grupe_uczniowska()
    {
        var security = new SecurityOptions();

        Assert.Contains("ZSE-Uczniowie", security.DeniedGroups);
        Assert.Equal(
            "ZSE-Uczniowie",
            RoleCatalog.FindDeniedGroup(new[] { "ZSE\\ZSE-Uczniowie" }, security.DeniedGroups));
        Assert.Null(RoleCatalog.FindDeniedGroup(new[] { "ZSE\\k.mazur" }, security.DeniedGroups));
    }

    [Fact]
    public void Polaczenie_bez_wss_jest_odrzucone_przed_upgradem()
    {
        var guard = new TransportSecurityGuard(new SecurityOptions { RequireSecureTransport = true });
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";

        Assert.False(guard.IsAllowed(context.Request));

        context.Request.Scheme = "https";
        Assert.True(guard.IsAllowed(context.Request));
    }

    private static WindowsAuthenticator WindowsAuthenticator(IWindowsGroupProvider provider) =>
        new(
            new SecurityOptions { Mode = SecurityOptions.WindowsMode },
            provider,
            new TransportSecurityGuard(new SecurityOptions { RequireSecureTransport = true }));

    private static DefaultHttpContext Context(string name)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, name) },
            authenticationType: "Negotiate");

        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static ZseAdminOptions SafeProductionOptions() => new()
    {
        Environment = "Production",
        UseMockData = false,
        UseMockDataForLogins = false,
        Directory = new DirectoryOptions { UseSsl = true },
        LoginHistory = new LoginHistoryOptions { Enabled = true, Source = "WindowsEventLog" }
    };

    private sealed class FakeGroupProvider : IWindowsGroupProvider
    {
        private readonly IReadOnlyList<string> _groups;

        public FakeGroupProvider(params string[] groups)
        {
            _groups = groups;
        }

        public IReadOnlyList<string> GetGroups(ClaimsPrincipal principal) => _groups;
    }
}
