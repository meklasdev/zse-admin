namespace ZseAdmin.Server.Infrastructure.Classrooms;

/// <summary>
/// Definicja pracowni z <c>classrooms.json</c>. Pracownia to kontekst szkolny,
/// a nie obiekt Active Directory — dlatego OU jest mapowane jawną regułą.
/// </summary>
public sealed class ClassroomDefinition
{
    /// <summary>Stabilny identyfikator konfiguracyjny, np. <c>prac-101</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Nazwa widoczna dla człowieka. Nie jest wyprowadzana z OU.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Jednostki organizacyjne (distinguishedName) przypisane do tej pracowni.</summary>
    public List<string> Ous { get; set; } = new();
}

/// <summary>Przypisanie komputera do pracowni wynikające z mapy OU.</summary>
public sealed record ClassroomAssignment(string ClassroomId, string ClassroomName);

/// <summary>Wynik budowy mapy: albo gotowa mapa, albo lista błędów konfiguracji.</summary>
public sealed record ClassroomMapBuildResult(ClassroomMap? Map, IReadOnlyList<string> Errors);

/// <summary>
/// Mapa OU → pracownia. Reguły (docs/architektura.md §6):
/// <list type="number">
/// <item>komputer dopasowany do OU z listy trafia do tej pracowni, także gdy OU jest kilka;</item>
/// <item>komputer poza listą nie znika, ale nie ma pracowni („poza pracownią”);</item>
/// <item>jedno OU może należeć tylko do jednej pracowni — kolizja to błąd konfiguracji;</item>
/// <item>przy zagnieżdżonych OU wygrywa najbardziej szczegółowe dopasowanie.</item>
/// </list>
/// </summary>
public sealed class ClassroomMap
{
    private readonly List<ClassroomDefinition> _classrooms;
    private readonly List<KeyValuePair<string, ClassroomDefinition>> _ous;

    private ClassroomMap(
        List<ClassroomDefinition> classrooms,
        List<KeyValuePair<string, ClassroomDefinition>> ous)
    {
        _classrooms = classrooms;
        _ous = ous;
    }

    public IReadOnlyList<ClassroomDefinition> Classrooms => _classrooms;

    /// <summary>
    /// Wbudowana mapa dla trybu danych przykładowych i testów. Odpowiada
    /// zawartości repozytoryjnego <c>classrooms.json</c>.
    /// </summary>
    public static ClassroomMap Development() =>
        Create(DevelopmentDefinitions()).Map
        ?? throw new InvalidOperationException("Wbudowana mapa pracowni jest niepoprawna.");

    /// <summary>Buduje mapę i waliduje konfigurację. Błędy nie rzucają wyjątku — trafiają do wyniku.</summary>
    public static ClassroomMapBuildResult Create(IEnumerable<ClassroomDefinition> definitions)
    {
        var errors = new List<string>();
        var classrooms = new List<ClassroomDefinition>();
        var ous = new List<KeyValuePair<string, ClassroomDefinition>>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ouOwners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            if (definition is null)
            {
                continue;
            }

            var id = (definition.Id ?? string.Empty).Trim();
            var name = (definition.Name ?? string.Empty).Trim();

            if (id.Length == 0)
            {
                errors.Add("Pracownia bez pola 'id' — identyfikator jest wymagany.");
                continue;
            }

            if (!seenIds.Add(id))
            {
                errors.Add($"Zduplikowany identyfikator pracowni '{id}'.");
                continue;
            }

            if (name.Length == 0)
            {
                errors.Add($"Pracownia '{id}' nie ma nazwy (pole 'name').");
                continue;
            }

            var classroom = new ClassroomDefinition
            {
                Id = id,
                Name = name,
                Ous = new List<string>()
            };

            var localOus = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rawOu in definition.Ous)
            {
                if (string.IsNullOrWhiteSpace(rawOu))
                {
                    errors.Add($"Pracownia '{id}' ma puste OU na liście.");
                    continue;
                }

                var ou = rawOu.Trim();

                if (!IsPlausibleDistinguishedName(ou))
                {
                    errors.Add(
                        $"Pracownia '{id}': '{ou}' nie wygląda na distinguishedName " +
                        "(oczekiwano np. OU=PRAC101,OU=Pracownie,DC=zse,DC=local).");
                    continue;
                }

                var normalized = NormalizeDistinguishedName(ou);

                // To samo OU dwa razy w jednej pracowni to nie błąd — po prostu je scalmy.
                if (!localOus.Add(normalized))
                {
                    continue;
                }

                if (ouOwners.TryGetValue(normalized, out var owner))
                {
                    errors.Add(
                        $"OU '{ou}' jest przypisane do dwóch pracowni: '{owner}' i '{id}'. " +
                        "Jedno OU może należeć tylko do jednej pracowni.");
                    continue;
                }

                ouOwners[normalized] = id;
                classroom.Ous.Add(ou);
                ous.Add(new KeyValuePair<string, ClassroomDefinition>(normalized, classroom));
            }

            classrooms.Add(classroom);
        }

        if (classrooms.Count == 0 && errors.Count == 0)
        {
            errors.Add("Konfiguracja nie zawiera żadnej pracowni.");
        }

        var map = errors.Count == 0 ? new ClassroomMap(classrooms, ous) : null;
        return new ClassroomMapBuildResult(map, errors);
    }

    /// <summary>Zwraca pracownię dla komputera o podanym distinguishedName albo <c>null</c> („poza pracownią”).</summary>
    public ClassroomAssignment? Resolve(string? distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return null;
        }

        var normalized = NormalizeDistinguishedName(distinguishedName);
        ClassroomDefinition? best = null;
        var bestLength = -1;

        foreach (var entry in _ous)
        {
            var ou = entry.Key;
            var matches = normalized.Length > ou.Length
                ? normalized.EndsWith("," + ou, StringComparison.Ordinal)
                : string.Equals(normalized, ou, StringComparison.Ordinal);

            // Najbardziej szczegółowe (najdłuższe) OU wygrywa przy zagnieżdżeniu.
            if (matches && ou.Length > bestLength)
            {
                bestLength = ou.Length;
                best = entry.Value;
            }
        }

        return best is null ? null : new ClassroomAssignment(best.Id, best.Name);
    }

    /// <summary>Pierwsze skonfigurowane OU pracowni (do prezentacji); <c>null</c> = pracownia bez OU.</summary>
    public string? PrimaryOu(string classroomId) =>
        _classrooms
            .FirstOrDefault(c => string.Equals(c.Id, classroomId, StringComparison.OrdinalIgnoreCase))
            ?.Ous.FirstOrDefault();

    /// <summary>Normalizuje DN: obcina spacje w RDN-ach i ujednolica wielkość liter.</summary>
    /// <remarks>
    /// Świadome ograniczenie: nie obsługuje przecinków escapowanych w wartościach RDN.
    /// W praktyce DN komputerów szkolnych tak nie wyglądają, a prostota jest tu ważniejsza
    /// niż pełna implementacja RFC 4514.
    /// </remarks>
    internal static string NormalizeDistinguishedName(string distinguishedName) =>
        string.Join(
            ",",
            distinguishedName
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim().ToUpperInvariant()));

    private static bool IsPlausibleDistinguishedName(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || separator == part.Length - 1)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<ClassroomDefinition> DevelopmentDefinitions() => new List<ClassroomDefinition>
    {
        new()
        {
            Id = "prac-101",
            Name = "Pracownia 101",
            Ous = new List<string> { "OU=PRAC101,OU=Pracownie,DC=zse,DC=local" }
        },
        new()
        {
            Id = "prac-102",
            Name = "Pracownia 102",
            Ous = new List<string> { "OU=PRAC102,OU=Pracownie,DC=zse,DC=local" }
        },
        new()
        {
            Id = "prac-205",
            Name = "Pracownia 205",
            Ous = new List<string> { "OU=PRAC205,OU=Pracownie,DC=zse,DC=local" }
        }
    };
}
