using ZseAdmin.Server.Infrastructure.Classrooms;
using Xunit;

namespace ZseAdmin.Tests;

/// <summary>
/// Reguły mapowania OU → pracownia z docs/architektura.md §6. To one decydują,
/// czy komputer „znika” z widoku — dlatego mają własne testy.
/// </summary>
public sealed class ClassroomMapTests
{
    private static ClassroomDefinition Classroom(string id, string name, params string[] ous) => new()
    {
        Id = id,
        Name = name,
        Ous = ous.ToList()
    };

    [Fact]
    public void Nauczyciel_ma_przypisane_pracownie_case_insensitive()
    {
        var map = ClassroomMap.Create(new[]
        {
            new ClassroomDefinition
            {
                Id = "prac-101",
                Name = "Pracownia 101",
                Ous = new List<string> { "OU=PRAC101,OU=Pracownie,DC=zse,DC=local" },
                Teachers = new List<string> { "k.mazur" }
            },
            new ClassroomDefinition
            {
                Id = "prac-102",
                Name = "Pracownia 102",
                Ous = new List<string> { "OU=PRAC102,OU=Pracownie,DC=zse,DC=local" },
                Teachers = new List<string> { "K.MAZUR" }
            }
        }).Map!;

        var mine = map.ClassroomIdsForTeacher("K.Mazur");

        Assert.Equal(new[] { "prac-101", "prac-102" }, mine.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(map.ClassroomIdsForTeacher("nikt.taki"));
        Assert.Empty(map.ClassroomIdsForTeacher(""));
    }

    [Fact]
    public void Pusty_wpis_nauczyciela_to_blad_konfiguracji()
    {
        var result = ClassroomMap.Create(new[]
        {
            new ClassroomDefinition
            {
                Id = "prac-101",
                Name = "Pracownia 101",
                Ous = new List<string> { "OU=PRAC101,OU=Pracownie,DC=zse,DC=local" },
                Teachers = new List<string> { "k.mazur", "  " }
            }
        });

        Assert.Null(result.Map);
        Assert.Contains(result.Errors, e => e.Contains("teachers", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplikaty_nauczycieli_sa_skladane_case_insensitive()
    {
        var result = ClassroomMap.Create(new[]
        {
            new ClassroomDefinition
            {
                Id = "prac-101",
                Name = "Pracownia 101",
                Ous = new List<string> { "OU=PRAC101,OU=Pracownie,DC=zse,DC=local" },
                Teachers = new List<string> { "k.mazur", "K.Mazur", "k.mazur" }
            }
        });

        Assert.NotNull(result.Map);
        Assert.Single(result.Map!.Classrooms[0].Teachers);
    }

    [Fact]
    public void Komputer_w_OU_pracowni_dostaje_przypisanie()
    {
        var map = ClassroomMap.Create(new[]
        {
            Classroom("prac-101", "Pracownia 101", "OU=PRAC101,OU=Pracownie,DC=zse,DC=local")
        }).Map!;

        var assignment = map.Resolve("CN=KS-101-01,OU=PRAC101,OU=Pracownie,DC=zse,DC=local");

        Assert.NotNull(assignment);
        Assert.Equal("prac-101", assignment!.ClassroomId);
        Assert.Equal("Pracownia 101", assignment.ClassroomName);
    }

    [Fact]
    public void Komputer_poza_znanym_OU_nie_dostaje_pracowni_ale_nie_rzuca()
    {
        var map = ClassroomMap.Create(new[]
        {
            Classroom("prac-101", "Pracownia 101", "OU=PRAC101,OU=Pracownie,DC=zse,DC=local")
        }).Map!;

        // „poza pracownią” = null, a nie wyjątek ani ciche przypisanie do pierwszej pracowni.
        Assert.Null(map.Resolve("CN=KS-SERWER-01,OU=Serwerownia,DC=zse,DC=local"));
        Assert.Null(map.Resolve(null));
        Assert.Null(map.Resolve(""));
    }

    [Fact]
    public void Jedno_OU_w_dwoch_pracowniach_to_blad_konfiguracji()
    {
        var result = ClassroomMap.Create(new[]
        {
            Classroom("prac-101", "Pracownia 101", "OU=PRAC101,OU=Pracownie,DC=zse,DC=local"),
            Classroom("prac-102", "Pracownia 102", "ou=prac101, ou=pracownie, dc=zse, dc=local")
        });

        Assert.Null(result.Map);
        Assert.Contains(result.Errors, e => e.Contains("dwóch pracowni", StringComparison.Ordinal));
    }

    [Fact]
    public void Jedna_pracownia_moze_miec_wiele_OU()
    {
        var result = ClassroomMap.Create(new[]
        {
            Classroom(
                "prac-101",
                "Pracownia 101",
                "OU=PRAC101,OU=Pracownie,DC=zse,DC=local",
                "OU=PRAC101B,OU=Pracownie,DC=zse,DC=local")
        });

        Assert.NotNull(result.Map);
        Assert.Equal("prac-101", result.Map!.Resolve("CN=KS-101B-01,OU=PRAC101B,OU=Pracownie,DC=zse,DC=local")?.ClassroomId);
    }

    [Fact]
    public void Przy_zagniezdzonych_OU_wygrywa_bardziej_szczegolowe()
    {
        var map = ClassroomMap.Create(new[]
        {
            Classroom("c-pracownie", "Pracownie", "OU=Pracownie,DC=zse,DC=local"),
            Classroom("prac-101", "Pracownia 101", "OU=PRAC101,OU=Pracownie,DC=zse,DC=local")
        }).Map!;

        Assert.Equal("prac-101", map.Resolve("CN=KS-101-01,OU=PRAC101,OU=Pracownie,DC=zse,DC=local")?.ClassroomId);
        Assert.Equal("c-pracownie", map.Resolve("CN=KS-1,OU=Inne,OU=Pracownie,DC=zse,DC=local")?.ClassroomId);
    }

    [Fact]
    public void Walidacja_wykrywa_brak_id_nazwy_i_zly_DN()
    {
        var result = ClassroomMap.Create(new[]
        {
            new ClassroomDefinition { Id = "", Name = "Bez id" },
            new ClassroomDefinition { Id = "prac-102", Name = "  " },
            Classroom("prac-103", "Pracownia 103", "PRAC103")
        });

        Assert.Null(result.Map);
        Assert.Contains(result.Errors, e => e.Contains("bez pola 'id'", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("nie ma nazwy", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("distinguishedName", StringComparison.Ordinal));
    }

    [Fact]
    public void Pusta_konfiguracja_to_blad_a_nie_pusta_mapa()
    {
        var result = ClassroomMap.Create(Array.Empty<ClassroomDefinition>());

        Assert.Null(result.Map);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Repozytoryjny_plik_pracowni_jest_poprawny()
    {
        // Pilnuje, że classrooms.json w repozytorium faktycznie przechodzi walidację przy starcie.
        var path = Path.Combine(RepositoryRoot(), "src", "ZseAdmin.Server", "classrooms.json");

        var loaded = ClassroomMapLoader.Load(path);

        Assert.NotNull(loaded);
        Assert.Empty(loaded!.Errors);
        Assert.NotNull(loaded.Map);
        Assert.Equal(3, loaded.Map!.Classrooms.Count);
    }

    [Fact]
    public void Brak_pliku_zwraca_null_a_nie_wyjatek()
    {
        Assert.Null(ClassroomMapLoader.Load(Path.Combine(Path.GetTempPath(), "zse-admin-nie-ma.json")));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ZseAdmin.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
