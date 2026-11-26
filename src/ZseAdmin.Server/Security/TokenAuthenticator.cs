using System.Security.Cryptography;
using System.Text;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Uwierzytelnianie tokenem przekazywanym w nagłówku <c>X-ZseAdmin-Token</c>
/// albo w parametrze zapytania <c>access_token</c> (przeglądarka nie potrafi ustawić nagłówka WS).
/// Wyłącznie development/CI — w trybie Windows rejestrujemy <see cref="WindowsAuthenticator"/>.
/// </summary>
public sealed class TokenAuthenticator : ITokenAuthenticator
{
    private readonly SecurityOptions _options;
    private readonly ITransportSecurityGuard _transport;

    public TokenAuthenticator(SecurityOptions options, ITransportSecurityGuard transport)
    {
        _options = options;
        _transport = transport;
    }

    public ClientIdentity? Authenticate(HttpRequest request)
    {
        var remoteAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString();
        var secure = _transport.IsSecure(request);

        if (!_options.RequireToken)
        {
            return RoleCatalog.CreateIdentity(
                "anonymous-dev", Roles.Administrator, null, remoteAddress, secure);
        }

        var token = request.Query["access_token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            token = request.Headers["X-ZseAdmin-Token"].FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        foreach (var entry in _options.Tokens)
        {
            if (!string.IsNullOrEmpty(entry.Token) && FixedTimeEquals(entry.Token, token))
            {
                return RoleCatalog.CreateIdentity(
                    entry.Name, entry.Role, null, remoteAddress, secure);
            }
        }

        return null;
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);

        if (expectedBytes.Length != actualBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
