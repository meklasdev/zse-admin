using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Bramka wdrożeniowa (fail-fast przy starcie): na produkcji tokeny z
/// appsettings.json są zabronione, transport musi być wss, a LDAP — LDAPS.
/// Czysta funkcja — w pełni testowalna bez hosta.
/// </summary>
public static class SecurityConfigurationValidator
{
    /// <summary>Zwraca listę błędów konfiguracji; pusta = konfiguracja produkcyjna poprawna.</summary>
    public static IReadOnlyList<string> Validate(
        bool isProduction,
        ZseAdminOptions zseAdmin,
        SecurityOptions security)
    {
        var errors = new List<string>();

        if (!isProduction)
        {
            return errors;
        }

        if (!string.Equals(security.Authentication.Mode, AuthenticationModes.ActiveDirectory, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                "Security:Authentication:Mode musi być 'ActiveDirectory' na produkcji (Windows/Kerberos + grupy AD). " +
                "Tokeny z appsettings.json są zabronione — zmień tryb albo uruchamiaj jako Development.");
        }

        if (security.Tokens.Count > 0)
        {
            errors.Add(
                "Security:Tokens nie może zawierać wpisów na produkcji — token w appsettings.json jest widoczny " +
                "dla każdego z dostępem do pliku. Usuń sekcję Tokens z appsettings.Production.json.");
        }

        if (!security.RequireSecureTransport)
        {
            errors.Add(
                "Security:RequireSecureTransport musi być 'true' na produkcji — połączenie /ws dozwolone wyłącznie przez wss://.");
        }

        if (zseAdmin.UseMockData)
        {
            errors.Add(
                "ZseAdmin:UseMockData=true na produkcji — serwer pokazywałby dane przykładowe jako dane szkoły.");
        }

        if (!zseAdmin.UseMockData && !zseAdmin.Directory.UseSsl)
        {
            errors.Add(
                "ZseAdmin:Directory:UseSsl musi być 'true' na produkcji — bez LDAPS ruch do kontrolera domeny jest jawny, " +
                "a zmiana hasła (unicodePwd) niemożliwa.");
        }

        return errors;
    }
}
