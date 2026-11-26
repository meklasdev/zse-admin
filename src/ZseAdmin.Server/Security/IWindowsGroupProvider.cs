using System.Security.Claims;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Port odczytu grup z tożsamości Windows. Wydzielony po to, żeby mapowanie grup → rola
/// było testowalne bez domeny (fake w testach zwraca zwykłe nazwy).
/// </summary>
public interface IWindowsGroupProvider
{
    IReadOnlyList<string> GetGroups(ClaimsPrincipal principal);
}
