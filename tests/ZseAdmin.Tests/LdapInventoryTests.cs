using System.Text;
using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Classrooms;
using ZseAdmin.Server.Infrastructure.Ldap;
using Xunit;

namespace ZseAdmin.Tests;

/// <summary>
/// Testy adaptera LDAP bez kontrolera domeny: atrybuty AD są bajtami, więc cała
/// interpretacja (czasy, flagi, OU, SID-y) musi być sprawdzona na poziomie jednostkowym.
/// </summary>
public sealed class LdapInventoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly ClassroomMap Map = ClassroomMap.Create(new[]
    {
        new ClassroomDefinition
        {
            Id = "prac-101",
            Name = "Pracownia 101",
            Ous = new List<string> { "OU=PRAC101,OU=Pracownie,DC=zse,DC=local" }
        }
    }).Map!;

    [Fact]
    public void Atrybuty_sa_dekodowane_z_bajtow()
    {
        var entry = Entry(
            "CN=KS-101-01,OU=PRAC101,OU=Pracownie,DC=zse,DC=local",
            ("cn", "KS-101-01"),
            ("operatingSystem", "Windows 10 Pro 22H2"),
            ("lastLogonTimestamp", "133908000000000000"));

        Assert.Equal("KS-101-01", entry.Text("cn"));
        Assert.Equal("Windows 10 Pro 22H2", entry.Text("operatingSystem"));
        Assert.NotNull(LdapInventoryReader.ReadFileTime(entry.Text("lastLogonTimestamp")));
        Assert.Null(entry.Text("nie-ma-takiego"));
    }

    [Fact]
    public void Komputer_z_OU_pracowni_jest_przypisany_a_poza_OU_zostaje_bez_pracowni()
    {
        var entries = new[]
        {
            Entry("CN=KS-101-01,OU=PRAC101,OU=Pracownie,DC=zse,DC=local", ("cn", "KS-101-01")),
            Entry("CN=KS-SERWER-01,OU=Serwerownia,DC=zse,DC=local", ("cn", "KS-SERWER-01"))
        };

        var computers = LdapInventoryReader.MapComputers(entries, Map, maxComputers: 100);

        Assert.Equal(2, computers.Count);
        Assert.Equal("prac-101", computers.Single(c => c.Name == "KS-101-01").ClassroomId);

        // Kryterium M1: komputer spoza OU widoczny, ale bez pracowni — nie znika.
        var server = computers.Single(c => c.Name == "KS-SERWER-01");
        Assert.Null(server.ClassroomId);
        Assert.Equal("CN=KS-SERWER-01,OU=Serwerownia,DC=zse,DC=local", server.DistinguishedName);
    }

    [Fact]
    public void Limit_max_komputerow_obcina_liste_a_inwentarz_oznacza_to_jako_parts()
    {
        var entries = Enumerable.Range(1, 10)
            .Select(i => Entry($"CN=KS-{i:00},OU=PRAC101,OU=Pracownie,DC=zse,DC=local", ("cn", $"KS-{i:00}")))
            .ToList();

        var computers = LdapInventoryReader.MapComputers(entries, Map, maxComputers: 4);

        Assert.Equal(4, computers.Count);
    }

    [Fact]
    public void Duplikaty_komputerow_sa_scalane()
    {
        var entries = new[]
        {
            Entry("CN=KS-101-01,OU=PRAC101,OU=Pracownie,DC=zse,DC=local", ("cn", "KS-101-01")),
            Entry("CN=KS-101-01,OU=PRAC101,OU=Pracownie,DC=zse,DC=local", ("cn", "KS-101-01"))
        };

        Assert.Single(LdapInventoryReader.MapComputers(entries, Map, maxComputers: 100));
    }

    [Fact]
    public void Konto_AD_mapuje_grupy_OU_i_flage_wylaczenia()
    {
        var entry = Entry(
            "CN=Jan Kowalski,OU=Uczniowie,OU=ZSE,DC=zse,DC=local",
            ("sAMAccountName", "j.kowalski"),
            ("displayName", "Jan Kowalski"),
            ("mail", "j.kowalski@zse.local"),
            ("userAccountControl", "514"),
            ("lastLogonTimestamp", "133900000000000000"));

        var user = LdapInventoryReader.MapUser(entry, new[] { "Uczniowie", "Klasa-3TI" })!;

        Assert.Equal("j.kowalski", user.SamAccountName);
        Assert.False(user.Enabled); // 514 = 512 | 0x2 (ACCOUNTDISABLE)
        Assert.Equal("OU=Uczniowie,OU=ZSE,DC=zse,DC=local", user.OuDistinguishedName);
        Assert.Contains("Klasa-3TI", user.Groups);
        Assert.NotNull(user.LastLogonAtUtc);
    }

    [Fact]
    public void Konto_bez_samaccountname_jest_pomijane()
    {
        var entry = Entry("CN=Bez loginu,OU=Uczniowie,DC=zse,DC=local", ("displayName", "Bez loginu"));

        Assert.Null(LdapInventoryReader.MapUser(entry, Array.Empty<string>()));
    }

    [Fact]
    public void Grupy_przez_tokenGroups_sa_rozwiazywane_jednym_zapytaniem()
    {
        var ldap = new FakeLdapReader();
        var groupSid = new byte[] { 1, 5, 0, 0, 0, 0, 0, 5, 21, 0, 0, 0 };
        ldap.AddGroup("Klasa-3TI", groupSid);

        var reader = new LdapInventoryReader(ldap, Map, new DirectoryOptions { SearchBase = "DC=zse,DC=local" }, new FakeClock(Now));
        var entry = Entry(
            "CN=Jan Kowalski,OU=Uczniowie,DC=zse,DC=local",
            ("sAMAccountName", "j.kowalski"));

        var groups = reader.ReadGroups(entry with
        {
            Attributes = new Dictionary<string, IReadOnlyList<byte[]>>(StringComparer.OrdinalIgnoreCase)
            {
                ["tokenGroups"] = new List<byte[]> { groupSid }
            }
        });

        Assert.Equal(new[] { "Klasa-3TI" }, groups);
        Assert.Equal(1, ldap.SearchCount);
    }

    [Fact]
    public void Grupa_spoza_domeny_nie_znika_tylko_pokazuje_SID()
    {
        var ldap = new FakeLdapReader();
        var foreignSid = new byte[] { 1, 2, 0, 0, 0, 0, 0, 5, 32, 0, 0, 0 };

        var reader = new LdapInventoryReader(ldap, Map, new DirectoryOptions { SearchBase = "DC=zse,DC=local" }, new FakeClock(Now));
        var groups = reader.ResolveGroupNames(new[] { foreignSid });

        Assert.Single(groups);
        Assert.StartsWith("S-1-5-", groups[0]);
    }

    [Fact]
    public void Konto_w_ad_nieistniejace_daje_null_a_nieblad()
    {
        var reader = CreateReader(new FakeLdapReader());

        var user = reader.FindUserAsync("nie.ma.takiego").GetAwaiter().GetResult();

        Assert.Null(user);
    }

    [Fact]
    public void Wartosc_filtra_LDAP_jest_escapowana()
    {
        Assert.Equal("a\\2ab\\28c\\29d\\5ce", LdapInventoryReader.EscapeFilterValue("a*b(c)d\\e"));
    }

    [Fact]
    public void Filtr_SID_jest_escapowany_bajt_po_bajcie()
    {
        Assert.Equal("\\01\\05\\00", LdapInventoryReader.EscapeBinary(new byte[] { 1, 5, 0 }));
    }

    [Fact]
    public void Niepoprawny_FILETIME_nie_wysadza_odczytu()
    {
        Assert.Null(LdapInventoryReader.ReadFileTime("0"));
        Assert.Null(LdapInventoryReader.ReadFileTime("to-nie-liczba"));
        Assert.Null(LdapInventoryReader.ReadFileTime(null));
    }

    [Fact]
    public void Komputer_bez_wczesniejszej_obserwacji_to_brak_danych()
    {
        var inventory = new FakeInventory(new AdInventory(
            new[] { new AdComputer("KS-101-01", "CN=KS-101-01,OU=PRAC101", "prac-101", "Pracownia 101", "10.0.0.1", "Windows 10", null) },
            Truncated: false,
            TotalComputers: 1));

        var directory = new AdComputerDirectory(inventory, new FakeProbe(), new FakeClock(Now), new StatusProbeOptions());

        var result = directory.ListAsync(null).GetAwaiter().GetResult();
        var computer = Assert.Single(result.Items);

        Assert.Equal(ComputerStatus.Unknown, computer.Status);
        Assert.Null(computer.StatusObservedAtUtc);
    }

    [Fact]
    public void Stacja_bez_odpowiedzi_jest_offline_z_czasem_obserwacji()
    {
        var inventory = new FakeInventory(new AdInventory(
            new[] { new AdComputer("KS-101-01", "CN=KS-101-01,OU=PRAC101", "prac-101", "Pracownia 101", "10.0.0.1", "Windows 10", null) },
            Truncated: false,
            TotalComputers: 1));

        var directory = new AdComputerDirectory(inventory, new FakeProbe(online: false), new FakeClock(Now), new StatusProbeOptions());

        var computer = Assert.Single(directory.ListAsync(null).GetAwaiter().GetResult().Items);

        Assert.Equal(ComputerStatus.Offline, computer.Status);
        Assert.Equal(Now, computer.StatusObservedAtUtc);
    }

    [Fact]
    public void Truncation_inwentarza_oznacza_dane_jako_czesciowe()
    {
        var inventory = new FakeInventory(new AdInventory(
            new[] { new AdComputer("KS-101-01", "CN=KS-101-01,OU=PRAC101", "prac-101", "Pracownia 101", "10.0.0.1", "Windows 10", null) },
            Truncated: true,
            TotalComputers: 5000));

        var directory = new AdComputerDirectory(inventory, new FakeProbe(), new FakeClock(Now), new StatusProbeOptions());

        var result = directory.ListAsync(null).GetAwaiter().GetResult();

        Assert.Equal(DataCompleteness.Partial, result.Metadata.Completeness);
        Assert.Contains("5000", result.Metadata.Note);
    }

    [Fact]
    public void Zmiana_stanu_jest_zwracana_raz_a_nie_w_kazdym_cyklu()
    {
        var inventory = new FakeInventory(new AdInventory(
            new[] { new AdComputer("KS-101-01", "CN=KS-101-01,OU=PRAC101", "prac-101", "Pracownia 101", "10.0.0.1", "Windows 10", null) },
            Truncated: false,
            TotalComputers: 1));

        var directory = new AdComputerDirectory(inventory, new FakeProbe(online: true), new FakeClock(Now), new StatusProbeOptions());

        Assert.Single(directory.RefreshStatusesAsync().GetAwaiter().GetResult());
        Assert.Empty(directory.RefreshStatusesAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public void Kolejne_zadania_korzystaja_z_migawki_stanu_a_nie_pinguja_ciagle()
    {
        var inventory = new FakeInventory(new AdInventory(
            new[] { new AdComputer("KS-101-01", "CN=KS-101-01,OU=PRAC101", "prac-101", "Pracownia 101", "10.0.0.1", "Windows 10", null) },
            Truncated: false,
            TotalComputers: 1));

        var probe = new FakeProbe();
        var directory = new AdComputerDirectory(inventory, probe, new FakeClock(Now), new StatusProbeOptions());

        directory.ListAsync(null).GetAwaiter().GetResult();
        directory.ListAsync(null).GetAwaiter().GetResult();

        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void Przerwane_sprawdzanie_nie_oznacza_stacji_jako_offline()
    {
        var inventory = new FakeInventory(new AdInventory(
            new[] { new AdComputer("KS-101-01", "CN=KS-101-01,OU=PRAC101", "prac-101", "Pracownia 101", "10.0.0.1", "Windows 10", null) },
            Truncated: false,
            TotalComputers: 1));

        // Pusty wynik = budżet sprawdzania minął, zanim stacja odpowiedziała.
        var directory = new AdComputerDirectory(inventory, new FakeProbe(empty: true), new FakeClock(Now), new StatusProbeOptions());

        var result = directory.ListAsync(null).GetAwaiter().GetResult();
        var computer = Assert.Single(result.Items);

        Assert.Equal(ComputerStatus.Unknown, computer.Status);
        Assert.Equal(DataCompleteness.Unavailable, result.Metadata.Completeness);
    }

    private static LdapInventoryReader CreateReader(ILdapDirectoryReader ldap) =>
        new(ldap, Map, new DirectoryOptions { SearchBase = "DC=zse,DC=local" }, new FakeClock(Now));

    private static LdapEntry Entry(string dn, params (string Name, string Value)[] attributes)
    {
        var dictionary = attributes.ToDictionary(
            a => a.Name,
            a => (IReadOnlyList<byte[]>)new List<byte[]> { Encoding.UTF8.GetBytes(a.Value) },
            StringComparer.OrdinalIgnoreCase);

        return new LdapEntry(dn, dictionary);
    }

    private sealed class FakeLdapReader : ILdapDirectoryReader
    {
        private readonly Dictionary<string, (string Name, byte[] Sid)> _groups = new(StringComparer.OrdinalIgnoreCase);

        public int SearchCount { get; private set; }

        public void AddGroup(string name, byte[] sid)
        {
            var key = Convert.ToHexString(sid);
            _groups[key] = (name, sid);
        }

        public IReadOnlyList<LdapEntry> Search(
            string searchBase,
            string filter,
            IReadOnlyList<string> attributes,
            CancellationToken cancellationToken = default)
        {
            SearchCount++;

            // Fake rozpoznaje zapytanie po SID-ach zaszytych w filtrze.
            var result = new List<LdapEntry>();
            foreach (var (key, group) in _groups)
            {
                if (filter.Contains(LdapInventoryReader.EscapeBinary(group.Sid), StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new LdapEntry(
                        $"CN={group.Name},OU=Grupy,DC=zse,DC=local",
                        new Dictionary<string, IReadOnlyList<byte[]>>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["cn"] = new List<byte[]> { Encoding.UTF8.GetBytes(group.Name) },
                            ["objectSid"] = new List<byte[]> { group.Sid }
                        }));
                }
            }

            return result;
        }

        public LdapEntry? FindByDistinguishedName(
            string distinguishedName,
            IReadOnlyList<string> attributes,
            CancellationToken cancellationToken = default) => null;
    }

    private sealed class FakeInventory : IAdInventoryReader
    {
        private readonly AdInventory _inventory;

        public FakeInventory(AdInventory inventory)
        {
            _inventory = inventory;
        }

        public Task<AdInventory> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_inventory);

        public Task<AdUserDto?> FindUserAsync(string samAccountName, CancellationToken cancellationToken = default) =>
            Task.FromResult<AdUserDto?>(null);
    }

    private sealed class FakeProbe : IComputerStatusProbe
    {
        private readonly bool _online;
        private readonly bool _empty;

        public FakeProbe(bool online = true, bool empty = false)
        {
            _online = online;
            _empty = empty;
        }

        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<string, bool>> ProbeAsync(
            IReadOnlyCollection<string> hosts,
            TimeSpan budget,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            return Task.FromResult<IReadOnlyDictionary<string, bool>>(
                _empty
                    ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    : hosts.ToDictionary(h => h, _ => _online, StringComparer.OrdinalIgnoreCase));
        }
    }
}
