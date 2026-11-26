namespace ZseAdmin.Contracts.Protocol;

/// <summary>Stałe nazwy typów wiadomości. Zmiana nazwy to zmiana protokołu — patrz docs/api-websocket.md.</summary>
public static class MessageTypes
{
    // Serwer → klient
    public const string Welcome = "welcome";
    public const string Error = "error";
    public const string ComputerStatusEvent = "event.computer.status";

    // Klient → serwer (i odpowiedź z tym samym typem)
    public const string Ping = "ping";
    public const string ClassroomsList = "classrooms.list";
    public const string ComputersList = "computers.list";
    public const string LoginsQuery = "logins.query";
    public const string UsersGet = "users.get";

    // Akcje i audyt (etapy M3/M4)
    public const string ComputersAction = "computers.action";
    public const string UsersResetPassword = "users.resetPassword";
    public const string UsersAddToGroup = "users.addToGroup";
    public const string AuditQuery = "audit.query";
}
