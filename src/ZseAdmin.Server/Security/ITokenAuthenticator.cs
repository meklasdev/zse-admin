namespace ZseAdmin.Server.Security;

public interface ITokenAuthenticator
{
    /// <summary>Zwraca tożsamość albo <c>null</c>, gdy połączenie nie jest uwierzytelnione.</summary>
    ClientIdentity? Authenticate(HttpRequest request);
}
