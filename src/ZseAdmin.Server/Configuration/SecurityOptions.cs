namespace ZseAdmin.Server.Configuration;

/// <summary>Tryby uwierzytelniania (M3).</summary>
public static class AuthenticationModes
{
    /// <summary>Statyczne tokeny z rolami — wyłącznie do developmentu; na produkcji serwer nie startuje.</summary>
    public const string DevelopmentTokens = "DevelopmentTokens";

    /// <summary>Uwierzytelnianie Windows/Kerberos (Negotiate) + rola z grup AD (<c>GroupMapping</c>).</summary>
    public const string ActiveDirectory = "ActiveDirectory";
}

/// <summary>Uwierzytelnianie i transport (etap M3).</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public AuthenticationOptions Authentication { get; set; } = new();

    /// <summary>Mapowanie grup AD na role (używane w trybie <see cref="AuthenticationModes.ActiveDirectory"/>).</summary>
    public GroupMappingOptions GroupMapping { get; set; } = new();

    /// <summary>
    /// Gdy <c>true</c>, połączenie <c>/ws</c> po niezaszyfrowanym kanale jest odrzucone (wymagane <c>wss://</c>).
    /// Na produkcji musi być <c>true</c> — wymusza to <c>SecurityConfigurationValidator</c> przy starcie.
    /// </summary>
    public bool RequireSecureTransport { get; set; }

    /// <summary>
    /// Ufaj nagłówkowi <c>X-Forwarded-Proto: https</c> (terminacja TLS na IIS/reverse proxy).
    /// Włącz tylko wtedy, gdy serwer faktycznie stoi za zaufanym proxy.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; } = true;

    /// <summary>Tokeny wyłącznie do developmentu. Na produkcji lista musi być pusta.</summary>
    public List<TokenEntry> Tokens { get; set; } = new();
}

public sealed class AuthenticationOptions
{
    /// <summary><see cref="AuthenticationModes.DevelopmentTokens"/> (dev) albo <see cref="AuthenticationModes.ActiveDirectory"/> (produkcja).</summary>
    public string Mode { get; set; } = AuthenticationModes.DevelopmentTokens;
}

public sealed class GroupMappingOptions
{
    /// <summary>Grupa AD dająca rolę Administrator.</summary>
    public string AdministratorGroup { get; set; } = "ZSE-Admin-Administratorzy";

    /// <summary>Grupa AD dająca rolę Nauczyciel. Brak w obu grupach = połączenie odrzucone.</summary>
    public string TeacherGroup { get; set; } = "ZSE-Admin-Nauczyciele";

    /// <summary>Grupa AD dająca rolę ReadOnly (dyżurka/sekretariat). Puste = rola niedostępna.</summary>
    public string? ReadOnlyGroup { get; set; } = "ZSE-Admin-Podglad";
}

public sealed class TokenEntry
{
    public string Name { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string Role { get; set; } = ZseAdmin.Server.Security.Roles.ReadOnly;
}
