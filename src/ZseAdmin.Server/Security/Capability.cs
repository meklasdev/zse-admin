namespace ZseAdmin.Server.Security;

/// <summary>
/// Uprawnienia ziarniste. Odczyt, modyfikacja AD i akcje na stacjach są rozdzielone.
/// Wartości są stałymi protokołu — źródłem prawdy jest <see cref="ZseAdmin.Contracts.Capabilities"/>;
/// klasa zostaje dla zgodności odwołań w serwerze i testach.
/// </summary>
public static class Capability
{
    public const string ClassroomsRead = ZseAdmin.Contracts.Capabilities.ClassroomsRead;
    public const string ComputersRead = ZseAdmin.Contracts.Capabilities.ComputersRead;
    public const string ComputersControl = ZseAdmin.Contracts.Capabilities.ComputersControl;
    public const string LoginsRead = ZseAdmin.Contracts.Capabilities.LoginsRead;
    public const string UsersRead = ZseAdmin.Contracts.Capabilities.UsersRead;
    public const string UsersWrite = ZseAdmin.Contracts.Capabilities.UsersWrite;
    public const string AuditRead = ZseAdmin.Contracts.Capabilities.AuditRead;
}

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Teacher = "Teacher";
    public const string ReadOnly = "ReadOnly";
}
