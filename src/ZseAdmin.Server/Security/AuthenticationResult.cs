namespace ZseAdmin.Server.Security;

/// <summary>
/// Wynik uwierzytelnienia jednej próby połączenia. <see cref="RejectionCode"/> jest krótkim,
/// stabilnym kodem przyczyny (bez danych osobowych) i trafia wyłącznie do audytu serwera —
/// klient dostaje gołe HTTP 401, żeby odmowa nie ujawniała, czy konto lub grupa istnieje.
/// </summary>
public readonly record struct AuthenticationResult(ClientIdentity? Identity, string? RejectionCode)
{
    public bool Succeeded => Identity is not null;

    public static AuthenticationResult Success(ClientIdentity identity) => new(identity, null);

    public static AuthenticationResult Reject(string code) => new(null, code);
}

/// <summary>Kody odmów uwierzytelnienia (M3 + D10). Wartości są częścią wpisu audytu.</summary>
public static class RejectionCodes
{
    public const string NotAuthenticated = "not_authenticated";
    public const string TokenMissing = "token_missing";
    public const string TokenInvalid = "token_invalid";
    public const string RoleGroupMissing = "role_group_missing";
    public const string DeniedGroup = "denied_group";
}
