using System.Security.Cryptography;
using System.Text;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Uwierzytelnianie tokenem przekazywanym w nagłówku <c>X-ZseAdmin-Token</c>
/// albo w parametrze zapytania <c>access_token</c> (przeglądarka nie potrafi ustawić nagłówka WS).
/// </summary>
public sealed class TokenAuthenticator : ITokenAuthenticator
{
    private readonly SecurityOptions _options;

    public TokenAuthenticator(SecurityOptions options)
    {
        _options = options;
    }

    public ClientIdentity? Authenticate(HttpRequest request)
    {
        if (!_options.RequireToken)
        {
            return RoleCatalog.CreateIdentity("anonymous-dev", Roles.Administrator);
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
                return RoleCatalog.CreateIdentity(entry.Name, entry.Role);
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
