using Microsoft.AspNetCore.Http;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Uwierzytelnia żądanie połączenia <c>/ws</c>. Zwraca tożsamość albo <c>null</c>
/// (brak/zły poświadczenia → 401). Gdy usługa katalogowa jest niedostępna, rzuca
/// <see cref="ZseAdmin.Server.Abstractions.SourceUnavailableException"/> (→ 503).
/// </summary>
public interface IRequestAuthenticator
{
    Task<ClientIdentity?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken = default);
}
