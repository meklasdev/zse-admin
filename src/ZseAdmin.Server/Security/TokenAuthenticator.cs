using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Uwierzytelnianie tokenem (wyłącznie development): nagłówek <c>X-ZseAdmin-Token</c>
/// albo parametr <c>?access_token=</c>. Token jest porównywany w czasie stałym.
/// Na produkcji ten mechanizm jest wyłączony — <see cref="SecurityConfigurationValidator"/>
/// zatrzymuje serwer, a nawet w trybie zdegradowanym authenticator zwraca wtedy <c>null</c>.
/// </summary>
public sealed class TokenAuthenticator : IRequestAuthenticator
{
    private readonly SecurityOptions _options;

    public TokenAuthenticator(SecurityOptions options)
    {
        _options = options;
    }

    public Task<ClientIdentity?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_options.Authentication.Mode, AuthenticationModes.DevelopmentTokens, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<ClientIdentity?>(null);
        }

        var token = context.Request.Query["access_token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            token = context.Request.Headers["X-ZseAdmin-Token"].FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<ClientIdentity?>(null);
        }

        foreach (var entry in _options.Tokens)
        {
            if (!string.IsNullOrEmpty(entry.Token) && FixedTimeEquals(entry.Token, token))
            {
                return Task.FromResult<ClientIdentity?>(RoleCatalog.CreateIdentity(entry.Name, entry.Role));
            }
        }

        return Task.FromResult<ClientIdentity?>(null);
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
