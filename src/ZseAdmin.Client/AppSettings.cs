using System.IO;
using System.Text.Json;

namespace ZseAdmin.Client;

/// <summary>Konfiguracja klienta czytana z appsettings.json obok pliku exe.</summary>
public sealed class AppSettings
{
    public string ServerUrl { get; set; } = "ws://localhost:5080/ws";

    /// <summary>
    /// Token deweloperski (tryb DevelopmentTokens na serwerze). Na produkcji token jest zabroniony —
    /// ustaw <see cref="UseWindowsAuthentication"/> = true zamiast tokenu.
    /// </summary>
    public string? Token { get; set; } = "dev-admin-token";

    /// <summary>
    /// M3: uwierzytelnianie Windows/Kerberos (Negotiate) — klient używa poświadczeń zalogowanego
    /// użytkownika. Ustaw true dla produkcji (wss + Negotiate na serwerze).
    /// </summary>
    public bool UseWindowsAuthentication { get; set; }

    public static AppSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(
                       json,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }
}
