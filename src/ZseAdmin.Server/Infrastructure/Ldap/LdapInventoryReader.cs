using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Classrooms;

namespace ZseAdmin.Server.Infrastructure.Ldap;

/// <summary>
/// Czyta z AD komputery i konta i mapuje je na pracownie. Cała logika interpretacji
/// atrybutów żyje tutaj, a <see cref="ILdapDirectoryReader"/> tylko wykonuje zapytania —
/// dzięki temu testy nie potrzebują kontrolera domeny.
/// </summary>
public sealed class LdapInventoryReader : IAdInventoryReader
{
    /// <summary>Atrybuty komputerów potrzebne do listy, stanu i IP.</summary>
    public static readonly string[] ComputerAttributes =
    {
        "cn",
        "dNSHostName",
        "operatingSystem",
        "lastLogonTimestamp",
        "objectClass"
    };

    /// <summary>Atrybuty konta: tożsamość, stan, OU. Grupy są czytane osobno (tokenGroups).</summary>
    public static readonly string[] UserAttributes =
    {
        "sAMAccountName",
        "displayName",
        "mail",
        "userAccountControl",
        "distinguishedName",
        "lastLogonTimestamp",
        // tokenGroups wymaga uprawnień do czytania konstruowanych atrybutów; memberOf
        // jest fallbackiem, gdy konto usługi ich nie ma (bez zagnieżdżenia grup).
        "tokenGroups",
        "memberOf"
    };

    private const int AccountDisabledFlag = 0x2;

    private readonly ILdapDirectoryReader _ldap;
    private readonly ClassroomMap _classrooms;
    private readonly DirectoryOptions _options;
    private readonly IClock _clock;
    private readonly Dictionary<string, (DateTimeOffset ReadAt, IReadOnlyList<AdComputer> Computers, int Total)> _cache =
        new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public LdapInventoryReader(
        ILdapDirectoryReader ldap,
        ClassroomMap classrooms,
        DirectoryOptions options,
        IClock clock)
    {
        _ldap = ldap;
        _classrooms = classrooms;
        _options = options;
        _clock = clock;
    }

    public Task<AdInventory> LoadAsync(CancellationToken cancellationToken = default)
    {
        var searchBase = ResolveSearchBase();

        lock (_sync)
        {
            if (_cache.TryGetValue(searchBase, out var cached))
            {
                var cacheAge = _clock.UtcNow - cached.ReadAt;
                if (cacheAge >= TimeSpan.Zero && cacheAge.TotalSeconds < Math.Max(0, _options.CacheSeconds))
                {
                    return Task.FromResult(new AdInventory(
                        cached.Computers,
                        cached.Total > cached.Computers.Count,
                        cached.Total));
                }
            }
        }

        var entries = _ldap.Search(
            searchBase,
            "(objectCategory=computer)",
            ComputerAttributes,
            cancellationToken);

        // Kolejność jest istotna: deduplikacja i limit siedzą w MapComputers, więc
        // „ile było w AD” liczymy z unikalnych cn-ów, a nie z liczby wpisów LDAP.
        var computers = MapComputers(entries, _classrooms, _options.MaxComputers, cancellationToken);
        var total = CountDistinctComputers(entries);

        lock (_sync)
        {
            _cache[searchBase] = (_clock.UtcNow, computers, total);
        }

        return Task.FromResult(new AdInventory(computers, total > computers.Count, total));
    }

    public Task<AdUserDto?> FindUserAsync(string samAccountName, CancellationToken cancellationToken = default)
    {
        var searchBase = ResolveSearchBase();
        var escaped = EscapeFilterValue(samAccountName);

        // Zapytanie wyszukuje po użytkownikach, ale zwraca tokenGroups/memberOf — dlatego
        // atrybuty są jednym zestawem (UserAttributes), a grupy rozwiązujemy jednym dodatkowym
        // zapytaniem po objectSid, a nie N zapytaniami.
        var entries = _ldap.Search(
            searchBase,
            $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={escaped}))",
            UserAttributes,
            cancellationToken);

        var entry = entries.FirstOrDefault();
        if (entry is null)
        {
            return Task.FromResult<AdUserDto?>(null);
        }

        return Task.FromResult(MapUser(entry, ReadGroups(entry, cancellationToken)));
    }

    /// <summary>
    /// Zamienia wpisy LDAP na komputery z przypisaniem do pracowni. Wydzielone jako metoda
    /// statyczna, bo to najważniejsza reguła M1 — komputer poza OU nie znika.
    /// </summary>
    public static IReadOnlyList<AdComputer> MapComputers(
        IReadOnlyList<LdapEntry> entries,
        ClassroomMap classrooms,
        int maxComputers,
        CancellationToken cancellationToken = default)
    {
        var result = new List<AdComputer>(Math.Min(entries.Count, Math.Max(0, maxComputers)));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (result.Count >= Math.Max(0, maxComputers))
            {
                break;
            }

            var name = entry.Text("cn") ?? FirstRdnValue(entry.DistinguishedName);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            // Ten sam obiekt może wrócić dwa razy (np. przy replikacji/referencjach) — deduplikujemy.
            if (!seen.Add(name))
            {
                continue;
            }

            var assignment = classrooms.Resolve(entry.DistinguishedName);

            result.Add(new AdComputer(
                Name: name,
                DistinguishedName: entry.DistinguishedName,
                ClassroomId: assignment?.ClassroomId,
                ClassroomName: assignment?.ClassroomName,
                // dNSHostName to nazwa FQDN, nie adres — nie udajemy, że znamy IP. Stan i IP
                // uzupełnia sprawdzanie dostępności (IComputerStatusProbe) w M1.1.
                IpAddress: null,
                OperatingSystem: entry.Text("operatingSystem"),
                LastLogonAtUtc: ReadFileTime(entry.Text("lastLogonTimestamp"))));
        }

        return result
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Mapuje konto AD na DTO. Zwraca <c>null</c>, gdy wpis nie ma <c>sAMAccountName</c>
    /// (np. obiekt kontaktowy, który potrafi wpaść w filtr (<c>objectClass=user</c>)).</summary>
    public static AdUserDto? MapUser(LdapEntry entry, IReadOnlyList<string>? groups)
    {
        var sam = entry.Text("sAMAccountName");
        if (string.IsNullOrWhiteSpace(sam))
        {
            return null;
        }

        var uac = entry.Text("userAccountControl");

        return new AdUserDto(
            SamAccountName: sam,
            DisplayName: entry.Text("displayName"),
            Email: entry.Text("mail"),
            Enabled: !(int.TryParse(uac, out var value) && (value & AccountDisabledFlag) == AccountDisabledFlag),
            Groups: groups ?? Array.Empty<string>(),
            OuDistinguishedName: ParentDistinguishedName(entry.DistinguishedName),
            LastLogonAtUtc: ReadFileTime(entry.Text("lastLogonTimestamp")));
    }

    /// <summary>
    /// Czyta grupy przez <c>tokenGroups</c> (uwzględnia zagnieżdżenie) i rozwiązuje nazwy jednym
    /// zapytaniem po <c>objectSid</c>. Gdy atrybutu brak (brak praw lub pusty zbiór), wracamy
    /// do <c>memberOf</c> — ale wtedy zagnieżdżenia nie widać, więc to świadomy fallback.
    /// </summary>
    public IReadOnlyList<string> ReadGroups(LdapEntry entry, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<byte[]> sids = entry.Attributes.TryGetValue("tokenGroups", out var values)
            ? values
            : Array.Empty<byte[]>();

        if (sids.Count > 0)
        {
            var resolved = ResolveGroupNames(sids, cancellationToken);
            return resolved
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var groups = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var memberOf in entry.Texts("memberOf"))
        {
            var cn = FirstRdnValue(memberOf);
            if (!string.IsNullOrWhiteSpace(cn) && seen.Add(cn))
            {
                groups.Add(cn);
            }
        }

        return groups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Rozwiązuje SID-y grup na nazwy. Jedno zapytanie na wszystkie SID-y: przy 300 grupach
    /// oznacza to 1 żądanie LDAP zamiast 300 (kryterium M1: odpowiedź &lt; 2 s).
    /// SID-y nierozwiązane (grupy spoza domeny) wracają jako <c>S-1-5-…</c>, nie znikają.
    /// </summary>
    public IReadOnlyList<string> ResolveGroupNames(
        IReadOnlyList<byte[]> sids,
        CancellationToken cancellationToken = default)
    {
        var results = new List<string>(sids.Count);

        var filter = "(|" + string.Concat(sids.Select(sid => $"(objectSid={EscapeBinary(sid)})")) + ")";

        var entries = _ldap.Search(
            ResolveSearchBase(),
            filter,
            new[] { "cn", "objectSid" },
            cancellationToken);

        var bySid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in entries)
        {
            if (!group.Attributes.TryGetValue("objectSid", out var raw) || raw.Count == 0)
            {
                continue;
            }

            var cn = group.Text("cn");
            if (!string.IsNullOrWhiteSpace(cn))
            {
                bySid[Convert.ToHexString(raw[0])] = cn;
            }
        }

        foreach (var sid in sids)
        {
            var key = Convert.ToHexString(sid);
            results.Add(bySid.TryGetValue(key, out var name) ? name : FormatSid(sid));
        }

        return results;
    }

    /// <summary>Escapowanie wartości binarnej w filtrze LDAP (RFC 4515: każdy bajt jako <c>\xx</c>).</summary>
    public static string EscapeBinary(byte[] value)
    {
        // Binarne SID-y w filtrze AD zapisujemy jako ciąg: znak backslash + dwie cyfry hex.
        var builder = new System.Text.StringBuilder(value.Length * 3);
        foreach (var b in value)
        {
            builder.Append('\\').Append(b.ToString("x2"));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Czytelna postać SID-a (<c>S-1-5-21-…</c>) — używana, gdy grupy nie da się rozwiązać.
    /// </summary>
    public static string FormatSid(byte[] sid)
    {
        if (sid.Length < 8)
        {
            return Convert.ToHexString(sid);
        }

        var revision = sid[0];
        var subAuthorityCount = sid[1];
        var identifierAuthority = 0L;
        for (var index = 2; index < 8; index++)
        {
            identifierAuthority = (identifierAuthority << 8) | sid[index];
        }

        var builder = new System.Text.StringBuilder($"S-{revision}-{identifierAuthority}");
        for (var index = 0; index < subAuthorityCount; index++)
        {
            var offset = 8 + index * 4;
            if (offset + 4 > sid.Length)
            {
                break;
            }

            var value = (uint)(sid[offset] | (sid[offset + 1] << 8) | (sid[offset + 2] << 16) | (sid[offset + 3] << 24));
            builder.Append('-').Append(value);
        }

        return builder.ToString();
    }

    /// <summary>Liczba unikalnych komputerów w odpowiedzi LDAP (dedup po cn/nazwie).</summary>
    public static int CountDistinctComputers(IReadOnlyList<LdapEntry> entries)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var name = entry.Text("cn") ?? FirstRdnValue(entry.DistinguishedName);
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names.Count;
    }

    private string ResolveSearchBase()
    {
        if (!string.IsNullOrWhiteSpace(_options.SearchBase))
        {
            return _options.SearchBase!.Trim();
        }

        var domain = _options.Domain;
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new SourceUnavailableException(
                "Active Directory",
                "Brak konfiguracji LDAP: ustaw 'ZseAdmin:Directory:SearchBase' albo 'ZseAdmin:Directory:Domain'.");
        }

        return LdapDirectoryReader.DomainToSearchBase(domain);
    }

    /// <summary>Konwersja <c>lastLogonTimestamp</c> (FILETIME, 100 ns od 1601 UTC) na czas.</summary>
    public static DateTimeOffset? ReadFileTime(string? raw)
    {
        if (!long.TryParse(raw, out var fileTime) || fileTime <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromFileTime(fileTime).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Escapowanie wartości filtra LDAP (RFC 4515).</summary>
    public static string EscapeFilterValue(string value) =>
        value
            .Replace("\\", "\\5c", StringComparison.Ordinal)
            .Replace("*", "\\2a", StringComparison.Ordinal)
            .Replace("(", "\\28", StringComparison.Ordinal)
            .Replace(")", "\\29", StringComparison.Ordinal)
            .Replace("\0", "\\00", StringComparison.Ordinal);

    /// <summary>Zwraca DN rodzica (<c>CN=x,OU=a,DC=b</c> → <c>OU=a,DC=b</c>).</summary>
    public static string? ParentDistinguishedName(string distinguishedName)
    {
        var separator = distinguishedName.IndexOf(',');
        return separator < 0 || separator == distinguishedName.Length - 1
            ? null
            : distinguishedName[(separator + 1)..].Trim();
    }

    private static string? FirstRdnValue(string distinguishedName)
    {
        var first = distinguishedName.Split(',')[0];
        var separator = first.IndexOf('=');
        return separator < 0 ? first.Trim() : first[(separator + 1)..].Trim();
    }
}
