namespace ZseAdmin.Server.Configuration;

/// <summary>
/// Uwierzytelnianie MVP: statyczne tokeny z rolami. Docelowo: Windows/Kerberos (Negotiate)
/// lub przynależność do grupy AD — patrz docs/uprawnienia-i-dane-osobowe.md.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Gdy <c>false</c>, każde połączenie dostaje rolę Administrator (tylko lokalny dev).</summary>
    public bool RequireToken { get; set; } = true;

    public List<TokenEntry> Tokens { get; set; } = new();
}

public sealed class TokenEntry
{
    public string Name { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string Role { get; set; } = ZseAdmin.Server.Security.Roles.ReadOnly;
}
