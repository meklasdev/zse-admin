using ZseAdmin.Server.Security;
using Xunit;

namespace ZseAdmin.Tests;

public sealed class RoleCatalogTests
{
    [Fact]
    public void Administrator_ma_wszystkie_uprawnienia()
    {
        var identity = RoleCatalog.CreateIdentity("admin.it", Roles.Administrator);

        Assert.True(identity.Has(Capability.ComputersControl));
        Assert.True(identity.Has(Capability.UsersWrite));
        Assert.True(identity.Has(Capability.LoginsRead));
        Assert.True(identity.Has(Capability.AuditRead));
    }

    [Fact]
    public void Nauczyciel_czyta_ale_nie_zmienia_AD_ani_stacji()
    {
        var identity = RoleCatalog.CreateIdentity("k.mazur", Roles.Teacher);

        Assert.True(identity.Has(Capability.LoginsRead));
        Assert.True(identity.Has(Capability.UsersRead));
        Assert.False(identity.Has(Capability.UsersWrite));
        Assert.False(identity.Has(Capability.ComputersControl));
    }

    [Fact]
    public void Nieznana_rola_nie_dostaje_zadnych_uprawnien()
    {
        var identity = RoleCatalog.CreateIdentity("ktos", "SuperUser");

        Assert.Empty(identity.Capabilities);
    }

    [Fact]
    public void Rola_student_nie_istnieje_w_katalogu_uprawnien()
    {
        // D10: nie tworzymy roli „Uczeń” z uprawnieniami. Nieznana rola = zero uprawnień.
        Assert.Empty(RoleCatalog.CapabilitiesFor("Student"));
        Assert.Empty(RoleCatalog.CapabilitiesFor("Uczen"));
        Assert.Empty(RoleCatalog.CapabilitiesFor("Uczeń"));
    }

    [Theory]
    [InlineData(Roles.Administrator)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.ReadOnly)]
    public void Topologia_i_listing_plikow_sa_dostepne_dla_wszystkich_rol_aplikacji(string role)
    {
        // D7 = C (topology.read) i D4 (files.read) — każda rola, która w ogóle się loguje.
        var identity = RoleCatalog.CreateIdentity("ktos", role);

        Assert.True(identity.Has(Capability.TopologyRead));
        Assert.True(identity.Has(Capability.FilesRead));
    }

    [Fact]
    public void Grupa_wykluczajaca_ma_pierwszenstwo_nad_rola()
    {
        var denied = new[] { "ZSE-Uczniowie" };

        Assert.Equal(
            "ZSE-Uczniowie",
            RoleCatalog.FindDeniedGroup(
                new[] { "ZSE\\ZSE-Uczniowie", "ZSE\\ZSE-Admin-Nauczyciele" },
                denied));
        Assert.Null(RoleCatalog.FindDeniedGroup(new[] { "ZSE\\ZSE-Admin-Nauczyciele" }, denied));
    }

    [Fact]
    public void Uprawnienia_sa_posortowane_i_unikalne()
    {
        var identity = RoleCatalog.CreateIdentity("admin.it", Roles.Administrator);

        Assert.Equal(identity.Capabilities.Distinct().Count(), identity.Capabilities.Count);
        Assert.True(identity.Capabilities.SequenceEqual(identity.Capabilities.OrderBy(c => c, StringComparer.Ordinal)));
    }
}
