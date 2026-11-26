using ZseAdmin.Contracts.Protocol;

namespace ZseAdmin.Client.Core.Connection;

public enum ConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3
}

/// <summary>Błąd zgłoszony przez serwer (wiadomość <c>error</c>) — nie mylić z brakiem połączenia.</summary>
public sealed class ServerErrorException : Exception
{
    public ServerErrorException(ErrorCode code, string message, string? detail = null)
        : base(message)
    {
        Code = code;
        Detail = detail;
    }

    public ErrorCode Code { get; }

    public string? Detail { get; }
}
