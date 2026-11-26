using System.Text.Json;

namespace ZseAdmin.Server.Infrastructure.Classrooms;

/// <summary>
/// Wczytuje mapę pracowni z pliku JSON (<c>classrooms.json</c>). Ścieżka jest względna
/// do katalogu roboczego procesu (czyli katalogu publikacji) albo bezwzględna.
/// </summary>
public static class ClassroomMapLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Gdy plik nie istnieje, zwraca <c>null</c> w wyniku (wołający decyduje, czy to błąd).
    /// Błędny JSON i błędy walidacji trafiają do <see cref="ClassroomMapBuildResult.Errors"/>.
    /// </summary>
    public static ClassroomMapBuildResult? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        List<ClassroomDefinition>? definitions;
        try
        {
            using var stream = File.OpenRead(fullPath);
            definitions = JsonSerializer.Deserialize<List<ClassroomDefinition>>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new ClassroomMapBuildResult(
                null,
                new[] { $"Nieczytelny plik pracowni '{fullPath}': {ex.Message}" });
        }
        catch (IOException ex)
        {
            return new ClassroomMapBuildResult(
                null,
                new[] { $"Nie udało się odczytać pliku pracowni '{fullPath}': {ex.Message}" });
        }

        if (definitions is null)
        {
            return new ClassroomMapBuildResult(
                null,
                new[] { $"Plik pracowni '{fullPath}' nie zawiera listy pracowni." });
        }

        return ClassroomMap.Create(definitions);
    }
}
