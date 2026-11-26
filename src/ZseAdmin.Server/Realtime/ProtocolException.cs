using ZseAdmin.Contracts.Protocol;

namespace ZseAdmin.Server.Realtime;

/// <summary>Błąd protokołu z kodem, który klient dostaje w wiadomości <c>error</c>.</summary>
public sealed class ProtocolException : Exception
{
    public ProtocolException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}
