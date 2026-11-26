using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Security;

namespace ZseAdmin.Server.Infrastructure.Ldap;

/// <summary>
/// Rozwiązuje grupy użytkownika przez LDAP <c>tokenGroups</c> (z zagnieżdżeniem) —
/// to samo podejście, którego używa odczyt kont (M1). SID-y rozwiązywane są jednym
/// zapytaniem po <c>objectSid</c>; grupy spoza domeny wracają jako <c>S-1-5-…</c>.
/// Brak uprawnień do odczytu atrybutu konstruowanego → fallback do <c>memberOf</c>
/// (bez zagnieżdżenia). Użytkownik nieistniejący → pusta lista (odmowa połączenia).
/// </summary>
public sealed class LdapGroupMembershipResolver : IAdGroupMembershipResolver
{
    private static readonly string[] UserAttributes = { "tokenGroups", "memberOf" };

    private readonly ILdapDirectoryReader _ldap;
    private readonly DirectoryOptions _options;

    public LdapGroupMembershipResolver(ILdapDirectoryReader ldap, DirectoryOptions options)
    {
        _ldap = ldap;
        _options = options;
    }

    public Task<IReadOnlyList<string>> GetGroupNamesAsync(
        string samAccountName,
        CancellationToken cancellationToken = default)
    {
        var searchBase = ResolveSearchBase();
        var escaped = LdapInventoryReader.EscapeFilterValue(samAccountName);

        var entries = _ldap.Search(
            searchBase,
            $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={escaped}))",
            UserAttributes,
            cancellationToken);

        var entry = entries.FirstOrDefault();
        if (entry is null)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        IReadOnlyList<byte[]> sids = entry.Attributes.TryGetValue("tokenGroups", out var values)
            ? values
            : Array.Empty<byte[]>();

        if (sids.Count > 0)
        {
            return Task.FromResult(ResolveSidNames(sids, searchBase, cancellationToken));
        }

        // Fallback: członkostwo bezpośrednie (bez zagnieżdżenia).
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

        return Task.FromResult<IReadOnlyList<string>>(groups
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private IReadOnlyList<string> ResolveSidNames(
        IReadOnlyList<byte[]> sids,
        string searchBase,
        CancellationToken cancellationToken)
    {
        var results = new List<string>(sids.Count);

        var filter = "(|" + string.Concat(sids.Select(sid => $"(objectSid={LdapInventoryReader.EscapeBinary(sid)})")) + ")";
        var entries = _ldap.Search(searchBase, filter, new[] { "cn", "objectSid" }, cancellationToken);

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
            results.Add(bySid.TryGetValue(Convert.ToHexString(sid), out var name)
                ? name
                : LdapInventoryReader.FormatSid(sid));
        }

        return results
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static string? FirstRdnValue(string distinguishedName)
    {
        var first = distinguishedName.Split(',')[0];
        var separator = first.IndexOf('=');
        return separator < 0 ? first.Trim() : first[(separator + 1)..].Trim();
    }
}
