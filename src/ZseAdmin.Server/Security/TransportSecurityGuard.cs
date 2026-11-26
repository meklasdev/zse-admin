using Microsoft.AspNetCore.Http;

namespace ZseAdmin.Server.Security;

public interface ITransportSecurityGuard
{
    /// <summary>Czy żądanie przyszło bezpiecznym kanałem (HTTPS/wss).</summary>
    bool IsSecure(HttpRequest request);

    /// <summary>Czy połączenie WebSocket może być przyjęte.</summary>
    bool IsAllowed(HttpRequest request);
}

/// <summary>
/// M3: w produkcji tylko <c>wss</c>. Za reverse proxy (IIS) <c>UseForwardedHeaders</c>
/// ustawia <c>request.IsHttps</c> na podstawie <c>X-Forwarded-Proto</c> od zaufanego proxy;
/// sam nagłówek od klienta nie jest dowodem, dlatego sprawdzamy oba źródła.
/// </summary>
public sealed class TransportSecurityGuard : ITransportSecurityGuard
{
    private readonly Configuration.SecurityOptions _options;

    public TransportSecurityGuard(Configuration.SecurityOptions options)
    {
        _options = options;
    }

    public bool IsSecure(HttpRequest request) =>
        // UseForwardedHeaders (zaufany reverse proxy) sam ustawia Scheme/IsHttps.
        // Nie ufamy nagłówkowi dostarczonemu bezpośrednio przez klienta.
        request.IsHttps;

    public bool IsAllowed(HttpRequest request) =>
        !_options.RequireSecureTransport || IsSecure(request);
}
