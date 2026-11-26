using System.IO;
using System.Text.Json;

namespace ZseAdmin.Client;

/// <summary>Konfiguracja klienta czytana z appsettings.json obok pliku exe.</summary>
public sealed class AppSettings
{
    public string ServerUrl { get; set; } = "ws://localhost:5080/ws";

    /// <summary>Token wyłącznie do developmentu. W produkcji ustaw <c>UseWindowsAuth = true</c>.</summary>
    public string? Token { get; set; } = "dev-admin-token";

    /// <summary>
    /// Gdy <c>true</c>, klient nie wysyła tokenu i używa zintegrowanego
    /// uwierzytelniania Windows. Wymaga <c>wss://</c> i <c>Security:Mode = Windows</c>.
    /// </summary>
    public bool UseWindowsAuth { get; set; }

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
