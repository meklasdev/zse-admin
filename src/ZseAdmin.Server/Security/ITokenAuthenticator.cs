namespace ZseAdmin.Server.Security;

public interface ITokenAuthenticator
{
    /// <summary>
    /// Zwraca wynik uwierzytelnienia: tożsamość sesji albo kod odmowy (bez danych osobowych),
    /// który <c>WsEndpoint</c> zapisuje w audycie jako <c>auth.rejected</c>.
    /// </summary>
    AuthenticationResult Authenticate(HttpRequest request);
}
