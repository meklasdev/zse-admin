using Microsoft.AspNetCore.Http;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Kontrola transportu (M3): na produkcji dozwolone wyłącznie połączenia <c>wss://</c>.
/// TLS może być terminowany na IIS/reverse proxy — wtedy decyduje zaufany nagłówek
/// <c>X-Forwarded-Proto: https</c> (wyłącznie gdy <see cref="Configuration.SecurityOptions.TrustForwardedHeaders"/>).
/// </summary>
public static class SecureTransport
{
    public static bool IsRequestAllowed(HttpContext context, Configuration.SecurityOptions options)
    {
        if (!options.RequireSecureTransport)
        {
            return true;
        }

        if (context.Request.IsHttps)
        {
            return true;
        }

        if (options.TrustForwardedHeaders)
        {
            var forwarded = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
            var first = forwarded?.Split(',')[0].Trim();

            if (string.Equals(first, "https", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
