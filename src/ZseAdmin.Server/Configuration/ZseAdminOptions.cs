namespace ZseAdmin.Server.Configuration;

/// <summary>Sekcja <c>ZseAdmin</c> w appsettings.json.</summary>
public sealed class ZseAdminOptions
{
    public const string SectionName = "ZseAdmin";

    /// <summary>Nazwa środowiska pokazywana w /healthz i logach.</summary>
    public string Environment { get; set; } = "Development";

    /// <summary>Jak używać danych testowych zamiast AD (MVP).</summary>
    public bool UseMockData { get; set; } = true;

    /// <summary>Interwał odpytywania stanu komputerów (sekundy). Minimum 5.</summary>
    public int StatusPollSeconds { get; set; } = 15;

    public FreshnessOptions Freshness { get; set; } = new();
}

/// <summary>Progi starzenia się danych. Powyżej progu dane są oznaczone jako nieaktualne.</summary>
public sealed class FreshnessOptions
{
    public int ComputersSeconds { get; set; } = 120;

    public int LoginsSeconds { get; set; } = 900;

    public int DirectorySeconds { get; set; } = 3600;
}
