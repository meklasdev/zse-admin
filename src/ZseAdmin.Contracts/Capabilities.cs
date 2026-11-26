namespace ZseAdmin.Contracts;

/// <summary>
/// Uprawnienia ziarniste — stałe protokołu współdzielone przez serwer i klienta.
/// Serwer (<c>ZseAdmin.Server.Security.Capability</c>) wskazuje na te same wartości,
/// więc klient może je ukrywać/pokazywać bez zależności od serwera.
/// </summary>
public static class Capabilities
{
    public const string ClassroomsRead = "classrooms.read";
    public const string ComputersRead = "computers.read";
    public const string ComputersControl = "computers.control";
    public const string LoginsRead = "logins.read";
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string AuditRead = "audit.read";
}
