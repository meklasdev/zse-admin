namespace ZseAdmin.Contracts.Protocol;

/// <summary>Kody błędów protokołu. Nazwy są stabilne — klient mapuje je na komunikaty UI.</summary>
public enum ErrorCode
{
    InvalidRequest = 0,
    Unauthorized = 1,
    Forbidden = 2,
    NotFound = 3,
    UpstreamUnavailable = 4,
    Internal = 5
}
