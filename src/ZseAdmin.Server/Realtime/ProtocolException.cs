using ZseAdmin.Contracts.Protocol;

namespace ZseAdmin.Server.Realtime;

/// <summary>Błąd protokołu z kodem, który klient dostaje w wiadomości <c>error</c>.</summary>
public sealed class ProtocolException : Exception
{
    public ProtocolException(ErrorCode code, string message, string? detail = null)
        : base(message)
    {
        Code = code;
        Detail = detail;
    }

    public ErrorCode Code { get; }

    /// <summary>Szczegół techniczny dla klienta (np. komunikat LDAP); nie zawiera sekretów.</summary>
    public string? Detail { get; }
}
