namespace ZseAdmin.Server.Security;

/// <summary>Uprawnienia ziarniste. Odczyt, modyfikacja AD i akcje na stacjach są rozdzielone.</summary>
public static class Capability
{
    public const string ClassroomsRead = "classrooms.read";
    public const string ComputersRead = "computers.read";
    public const string ComputersControl = "computers.control";
    public const string LoginsRead = "logins.read";
    public const string UsersRead = "users.read";
    public const string UsersWrite = "users.write";
    public const string AuditRead = "audit.read";
}

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Teacher = "Teacher";
    public const string ReadOnly = "ReadOnly";
}
