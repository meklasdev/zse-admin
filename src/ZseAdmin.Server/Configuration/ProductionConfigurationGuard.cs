namespace ZseAdmin.Server.Configuration;

/// <summary>
/// Bramka wdrożeniowa M3: produkcja nie startuje z tokenami, danymi przykładowymi,
/// zwykłym <c>ws://</c> ani z wyłączoną historią logowań. Fail-fast jest bezpieczniejszy
/// niż ciche uruchomienie niezabezpieczonego serwera.
/// </summary>
public static class ProductionConfigurationGuard
{
    public static IReadOnlyList<string> Validate(ZseAdminOptions options, SecurityOptions security)
    {
        var errors = new List<string>();

        if (!security.IsWindowsMode)
        {
            errors.Add("Security:Mode musi być 'Windows' (uwierzytelnianie Kerberos/grupy AD).");
        }

        if (security.Tokens.Count > 0)
        {
            errors.Add("Security:Tokens nie może zawierać tokenów w produkcji (wyłącz uwierzytelnianie tokenowe).");
        }

        if (!security.RequireSecureTransport)
        {
            errors.Add("Security:RequireSecureTransport musi być true — połączenie bez wss jest odrzucane.");
        }

        if (options.UseMockData || options.UseMockDataForLogins)
        {
            errors.Add("ZseAdmin:UseMockData i UseMockDataForLogins muszą być false.");
        }

        if (!options.LoginHistory.Enabled ||
            string.Equals(options.LoginHistory.Source, "Mock", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ZseAdmin:LoginHistory musi być włączone i czytać z realnego źródła (nie Mock).");
        }

        if (!options.Directory.UseSsl)
        {
            errors.Add("ZseAdmin:Directory:UseSsl musi być true (LDAPS; zapis unicodePwd wymaga szyfrowanego LDAP).");
        }

        if (!security.RoleGroups.TryGetValue(SecurityOptions.TeacherGroup, out var teacherRole) ||
            !string.Equals(teacherRole, "Teacher", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Security:RoleGroups musi mapować '{SecurityOptions.TeacherGroup}' na rolę Teacher.");
        }

        return errors;
    }
}
