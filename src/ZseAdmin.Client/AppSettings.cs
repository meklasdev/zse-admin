using System.IO;
using System.Text.Json;

namespace ZseAdmin.Client;

/// <summary>Konfiguracja klienta czytana z appsettings.json obok pliku exe.</summary>
public sealed class AppSettings
{
    public string ServerUrl { get; set; } = "ws://localhost:5080/ws";

    /// <summary>Token MVP. Docelowo: Windows/Kerberos zamiast tokenu — docs/uprawnienia-i-dane-osobowe.md.</summary>
    public string? Token { get; set; } = "dev-admin-token";

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
