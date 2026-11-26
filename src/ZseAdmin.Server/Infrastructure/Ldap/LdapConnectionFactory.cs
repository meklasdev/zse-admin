using System.DirectoryServices.Protocols;
using System.Net;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Ldap;

/// <summary>
/// Wspólna konfiguracja połączenia LDAP dla odczytu (M1) i zapisu (M4).
/// Bind jest leniwy — serwer startuje także bez osiągalnego kontrolera domeny,
/// a błąd pojawia się dopiero przy pierwszej operacji jako <c>upstreamUnavailable</c>.
/// </summary>
public sealed class LdapConnectionFactory
{
    private const int ProtocolVersion = 3;

    private readonly DirectoryOptions _options;

    public LdapConnectionFactory(DirectoryOptions options)
    {
        _options = options;
    }

    public LdapConnection Create()
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

        connection.Credential = BuildCredential();
        connection.Timeout = TimeSpan.FromSeconds(30);
        connection.Bind();

        return connection;
    }

    private LdapDirectoryIdentifier BuildIdentifier()
    {
        var host = _options.Host;
        var port = _options.Port > 0 ? _options.Port : (_options.UseSsl ? 636 : 389);
        var domain = string.IsNullOrWhiteSpace(_options.Domain)
            ? LdapDirectoryReader.DomainNameFromSearchBase(_options.SearchBase)
            : _options.Domain;

        if (!string.IsNullOrWhiteSpace(host))
        {
            return new LdapDirectoryIdentifier(host.Trim(), port, false, false);
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            return new LdapDirectoryIdentifier(domain!.Trim(), port, true, false);
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
                    ? LdapDirectoryReader.DomainNameFromSearchBase(_options.SearchBase)
                    : _options.Domain);
}
