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
    public void Uprawnienia_sa_posortowane_i_unikalne()
    {
        var identity = RoleCatalog.CreateIdentity("admin.it", Roles.Administrator);

        Assert.Equal(identity.Capabilities.Distinct().Count(), identity.Capabilities.Count);
        Assert.True(identity.Capabilities.SequenceEqual(identity.Capabilities.OrderBy(c => c, StringComparer.Ordinal)));
    }
}
