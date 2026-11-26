namespace ZseAdmin.Server.Configuration;

/// <summary>Sekcja <c>ZseAdmin</c> w appsettings.json.</summary>
public sealed class ZseAdminOptions
{
    public const string SectionName = "ZseAdmin";

    /// <summary>Nazwa środowiska pokazywana w /healthz i logach.</summary>
    public string Environment { get; set; } = "Development";

    /// <summary>Jak używać danych testowych zamiast AD (MVP).</summary>
    public bool UseMockData { get; set; } = true;

    /// <summary>
    /// Tryb przejściowy M1: prawdziwe AD, ale historia logowań jeszcze z danych przykładowych.
    /// Na produkcji ustaw <c>false</c> — wtedy serwer jawnie zwraca „historia niedostępna (etap M2)”.
    /// </summary>
    public bool UseMockDataForLogins { get; set; } = true;

    /// <summary>Interwał odpytywania stanu komputerów (sekundy). Minimum 5.</summary>
    public int StatusPollSeconds { get; set; } = 15;

    /// <summary>
    /// Definicja pracowni i mapowanie OU → pracownia. Plik wczytywany przy starcie;
    /// błąd konfiguracji zatrzymuje serwer (kryterium M1).
    /// </summary>
    public ClassroomsOptions Classrooms { get; set; } = new();

    public DirectoryOptions Directory { get; set; } = new();

    public StatusProbeOptions StatusProbe { get; set; } = new();

    public FreshnessOptions Freshness { get; set; } = new();
}

/// <summary>Konfiguracja pliku z pracowniami (ADR-0005).</summary>
public sealed class ClassroomsOptions
{
    /// <summary>Ścieżka do JSON-a z pracowniami; dla danych przykładowych nieużywana.</summary>
    public string Path { get; set; } = "classrooms.json";
}

/// <summary>Połączenie z Active Directory przez LDAP.</summary>
public sealed class DirectoryOptions
{
    /// <summary>Host LDAP, np. <c>zse-dc01.zse.local</c>. <c>null</c> = wybierz kontroler z domeny <see cref="Domain"/>.</summary>
    public string? Host { get; set; }

    /// <summary>Domena do wyboru kontrolera, gdy <see cref="Host"/> jest pusty (np. <c>zse.local</c>).</summary>
    public string? Domain { get; set; }

    public int Port { get; set; } = 389;

    /// <summary>Wymuś LDAPS (636). W produkcji powinno być <c>true</c>.</summary>
    public bool UseSsl { get; set; }

    /// <summary>Baza wyszukiwania, np. <c>DC=zse,DC=local</c>. Pusta = wyliczona z <see cref="Domain"/>.</summary>
    public string? SearchBase { get; set; }

    /// <summary>
    /// Kontekst uwierzytelniania konta usługi. Puste = bieżące konto procesu
    /// (zalecane: konto usługi z minimalnymi prawami).
    /// </summary>
    public string? ServiceAccountUser { get; set; }

    /// <summary>Hasło konta usługi. Trzymaj poza repozytorium (zmienne środowiskowe).</summary>
    public string? ServiceAccountPassword { get; set; }

    /// <summary>Rozmiar strony LDAP. Zabezpiecza przed limitem 1000 obiektów na zapytanie.</summary>
    public int PageSize { get; set; } = 500;

    /// <summary>Limit komputerów pobieranych z AD (ochrona przed niekontrolowanym rozmiarem odpowiedzi).</summary>
    public int MaxComputers { get; set; } = 2000;

    /// <summary>Czas ważności cache katalogu AD (sekundy).</summary>
    public int CacheSeconds { get; set; } = 60;
}

/// <summary>Sprawdzanie dostępności stacji (ICMP).</summary>
public sealed class StatusProbeOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Timeout pojedynczego sprawdzenia (ms).</summary>
    public int TimeoutMilliseconds { get; set; } = 1000;

    /// <summary>Ile stacji sprawdzać równolegle.</summary>
    public int MaxParallelism { get; set; } = 32;

    /// <summary>
    /// Budżet jednego cyklu sprawdzania (ms). Po jego przekroczeniu wynik jest częściowy —
    /// lepiej pokazać „brak danych” niż trzymać żądanie klienta w nieskończoność.
    /// </summary>
    public int BudgetMilliseconds { get; set; } = 8000;
}

/// <summary>Progi starzenia się danych. Powyżej progu dane są oznaczone jako nieaktualne.</summary>
public sealed class FreshnessOptions
{
    public int ComputersSeconds { get; set; } = 120;

    public int LoginsSeconds { get; set; } = 900;

    public int DirectorySeconds { get; set; } = 3600;
}
