using System.DirectoryServices.Protocols;
using System.Net;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Ldap;

/// <summary>
/// Jedyna implementacja <see cref="ILdapDirectoryReader"/> oparta na
/// <c>System.DirectoryServices.Protocols</c>. Wyszukiwania są stronicowane, a odpowiedzi
/// mapowane na <see cref="LdapEntry"/> — dalsza logika nie zna LDAP-a.
/// </summary>
public sealed class LdapDirectoryReader : ILdapDirectoryReader, ILdapWriter, IDisposable
{
    private const int ProtocolVersion = 3;

    private readonly DirectoryOptions _options;
    private readonly Lazy<LdapConnection> _connection;

    public LdapDirectoryReader(DirectoryOptions options)
    {
        _options = options;
        _connection = new Lazy<LdapConnection>(CreateConnection, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string SearchBase =>
        !string.IsNullOrWhiteSpace(_options.SearchBase)
            ? _options.SearchBase!.Trim()
            : DomainToSearchBase(_options.Domain);

    public IReadOnlyList<LdapEntry> Search(
        string searchBase,
        string filter,
        IReadOnlyList<string> attributes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new SearchRequest(searchBase, filter, SearchScope.Subtree, attributes.ToArray())
        {
            TimeLimit = TimeSpan.FromSeconds(30)
        };

        var pageControl = new PageResultRequestControl(Math.Clamp(_options.PageSize, 1, 1000));
        request.Controls.Add(pageControl);

        var results = new List<LdapEntry>();

        try
        {
            while (true)
            {
                var response = (SearchResponse)_connection.Value.SendRequest(request);
                results.AddRange(ToEntries(response));

                if (response.Controls.Length == 0)
                {
                    break;
                }

                var pageResponse = response.Controls
                    .OfType<PageResultResponseControl>()
                    .FirstOrDefault();

                if (pageResponse is null || pageResponse.Cookie.Length == 0)
                {
                    break;
                }

                pageControl.Cookie = pageResponse.Cookie;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (LdapException ex)
        {
            throw new SourceUnavailableException("Active Directory", $"Błąd LDAP: {ex.Message}", ex);
        }
        catch (DirectoryOperationException ex)
        {
            throw new SourceUnavailableException("Active Directory", $"Błąd operacji LDAP: {ex.Message}", ex);
        }

        return results;
    }

    public LdapEntry? FindByDistinguishedName(
        string distinguishedName,
        IReadOnlyList<string> attributes,
        CancellationToken cancellationToken = default)
    {
        var request = new SearchRequest(
            distinguishedName,
            "(objectClass=*)",
            SearchScope.Base,
            attributes.ToArray());

        try
        {
            var response = (SearchResponse)_connection.Value.SendRequest(request);
            cancellationToken.ThrowIfCancellationRequested();
            return ToEntries(response).FirstOrDefault();
        }
        catch (LdapException ex)
        {
            throw new SourceUnavailableException("Active Directory", $"Błąd LDAP: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Zmiana atrybutu (M4): <c>unicodePwd</c> wymaga kanału LDAPS, <c>member</c> — uprawnień
    /// do zapisu grupy. Błędy LDAP są rzucane jako <see cref="SourceUnavailableException"/>.
    /// </summary>
    public void Modify(
        string distinguishedName,
        LdapModification modification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new ModifyRequest(distinguishedName);
        var directoryModification = new DirectoryAttributeModification
        {
            Name = modification.Attribute
        };

        directoryModification.Operation = modification.Operation switch
        {
            LdapModificationOperation.Add => DirectoryAttributeOperation.Add,
            LdapModificationOperation.Replace => DirectoryAttributeOperation.Replace,
            LdapModificationOperation.Delete => DirectoryAttributeOperation.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(modification), "Nieznana operacja modyfikacji LDAP.")
        };

        foreach (var value in modification.Values)
        {
            directoryModification.Add(value);
        }

        request.Modifications.Add(directoryModification);

        try
        {
            _connection.Value.SendRequest(request);
        }
        catch (LdapException ex)
        {
            throw new SourceUnavailableException("Active Directory", $"Błąd LDAP (modify): {ex.Message}", ex);
        }
        catch (DirectoryOperationException ex)
        {
            throw new SourceUnavailableException("Active Directory", $"Błąd operacji LDAP (modify): {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            _connection.Value.Dispose();
        }
    }

    /// <summary>Zamienia odpowiedź LDAP na <see cref="LdapEntry"/> (publiczne dla testów formatu atrybutów).</summary>
    public static IReadOnlyList<LdapEntry> ToEntries(SearchResponse response)
    {
        var entries = new List<LdapEntry>(response.Entries.Count);

        foreach (SearchResultEntry entry in response.Entries)
        {
            var attributes = new Dictionary<string, IReadOnlyList<byte[]>>(StringComparer.OrdinalIgnoreCase);

            foreach (string name in entry.Attributes.AttributeNames)
            {
                var values = new List<byte[]>();
                var attribute = entry.Attributes[name];

                for (var index = 0; index < attribute.Count; index++)
                {
                    if (attribute[index] is byte[] bytes)
                    {
                        values.Add(bytes);
                    }
                }

                attributes[name] = values;
            }

            entries.Add(new LdapEntry(entry.DistinguishedName, attributes));
        }

        return entries;
    }

    /// <summary>Zamienia <c>OU=Pracownie,DC=zse,DC=local</c> na <c>zse.local</c>.</summary>
    public static string? DomainNameFromSearchBase(string? searchBase)
    {
        if (string.IsNullOrWhiteSpace(searchBase))
        {
            return null;
        }

        var parts = searchBase
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]);

        var domain = string.Join(".", parts);
        return domain.Length == 0 ? null : domain;
    }

    /// <summary>Zamienia <c>zse.local</c> na <c>DC=zse,DC=local</c> (pusta wartość → pusty string).</summary>
    public static string DomainToSearchBase(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return string.Empty;
        }

        return string.Join(
            ",",
            domain
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => $"DC={part.Trim()}"));
    }

    private LdapConnection CreateConnection()
    {
        var identifier = BuildIdentifier();
        var connection = new LdapConnection(identifier)
        {
            AuthType = AuthType.Negotiate,
            AutoBind = false
        };

        connection.SessionOptions.ProtocolVersion = ProtocolVersion;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        if (_options.UseSsl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }

        var credential = BuildCredential();
        connection.Credential = credential;
        connection.Timeout = TimeSpan.FromSeconds(30);
        connection.Bind();

        return connection;
    }

    private LdapDirectoryIdentifier BuildIdentifier()
    {
        var host = _options.Host;
        var port = _options.Port > 0 ? _options.Port : (_options.UseSsl ? 636 : 389);
        var domain = string.IsNullOrWhiteSpace(_options.Domain)
            ? DomainNameFromSearchBase(_options.SearchBase)
            : _options.Domain;

        if (!string.IsNullOrWhiteSpace(host))
        {
            return new LdapDirectoryIdentifier(host.Trim(), port, fullyQualifiedDnsHostName: false, connectionless: false);
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            // Bez hosta wybieramy kontroler domeny automatycznie.
            return new LdapDirectoryIdentifier(domain!.Trim(), port, fullyQualifiedDnsHostName: true, connectionless: false);
        }

        throw new SourceUnavailableException(
            "Active Directory",
            "Brak konfiguracji LDAP: ustaw 'ZseAdmin:Directory:Host' albo 'ZseAdmin:Directory:Domain'.");
    }

    private NetworkCredential BuildCredential() =>
        string.IsNullOrWhiteSpace(_options.ServiceAccountUser)
            ? CredentialCache.DefaultNetworkCredentials
            : new NetworkCredential(
                _options.ServiceAccountUser,
                _options.ServiceAccountPassword,
                string.IsNullOrWhiteSpace(_options.Domain)
                    ? DomainNameFromSearchBase(_options.SearchBase)
                    : _options.Domain);
}
