using ZseAdmin.Contracts;

namespace ZseAdmin.Server.Abstractions;

/// <summary>
/// Minimalny port LDAP używany przez serwer. Dzięki temu logika mapowania komputerów,
/// kont i grup jest testowalna bez kontrolera domeny, a <c>System.DirectoryServices.Protocols</c>
/// jest jedyną implementacją (<see cref="Infrastructure.Ldap.LdapDirectoryReader"/>).
/// </summary>
public interface ILdapDirectoryReader
{
    /// <summary>Wyszukuje obiekty w bazie konfiguracji. Filtry: <c>(objectCategory=computer)</c> itd.</summary>
    IReadOnlyList<LdapEntry> Search(
        string searchBase,
        string filter,
        IReadOnlyList<string> attributes,
        CancellationToken cancellationToken = default);

    /// <summary>Zwraca obiekt po distinguishedName albo <c>null</c>, gdy nie istnieje.</summary>
    LdapEntry? FindByDistinguishedName(
        string distinguishedName,
        IReadOnlyList<string> attributes,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Odczyt katalogu AD w formie gotowej dla serwera: komputery i konta wraz z mapowaniem
/// na pracownie. Implementacja LDAP nie zna protokołu ani DTO protokołu — zwraca byty domenowe.
/// </summary>
public interface IAdInventoryReader
{
    Task<AdInventory> LoadAsync(CancellationToken cancellationToken = default);

    Task<AdUserDto?> FindUserAsync(string samAccountName, CancellationToken cancellationToken = default);
}

/// <summary>Komputer z AD — bez stanu sieciowego (ten dokłada <see cref="StatusProbe"/>).</summary>
public sealed record AdComputer(
    string Name,
    string DistinguishedName,
    string? ClassroomId,
    string? ClassroomName,
    string? IpAddress,
    string? OperatingSystem,
    DateTimeOffset? LastLogonAtUtc);

/// <summary>Wynik odczytu katalogu: komputery oraz informacja, czy AD odpowiedział w całości.</summary>
public sealed record AdInventory(
    IReadOnlyList<AdComputer> Computers,
    bool Truncated,
    int TotalComputers)
{
    public static AdInventory Empty { get; } =
        new(Array.Empty<AdComputer>(), Truncated: false, TotalComputers: 0);
}

/// <summary>
/// Sprawdzanie dostępności stacji. Wydzielone od katalogu, bo w docelowej wersji
/// to inna technologia (ICMP/SMB/WMI) i inny harmonogram.
/// </summary>
public interface IComputerStatusProbe
{
    /// <summary>
    /// Zwraca stan każdej stacji: <c>true</c> = odpowiedziała, <c>false</c> = brak odpowiedzi.
    /// Stacje nieobecne w wyniku są traktowane jako „brak danych”, nie „offline”.
    /// </summary>
    /// <param name="hosts">Stacje do sprawdzenia.</param>
    /// <param name="budget">
    /// Maksymalny czas sprawdzania. Po jego przekroczeniu zwracany jest wynik częściowy —
    /// niesprawdzone stacje nie są raportowane jako offline, tylko zachowują poprzednią obserwację.
    /// </param>
    /// <param name="cancellationToken">Anulowanie żądania klienta.</param>
    Task<IReadOnlyDictionary<string, bool>> ProbeAsync(
        IReadOnlyCollection<string> hosts,
        TimeSpan budget,
        CancellationToken cancellationToken = default);
}
