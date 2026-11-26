namespace ZseAdmin.Server.Abstractions;

/// <summary>Wynik udanej akcji: stan przed, po oraz komunikat źródła dla UI.</summary>
public sealed record ActionChange(string Before, string After, string Message);

/// <summary>
/// Akcje na stacjach (M4). Błąd źródła jest wyjątkiem, nigdy „sukcesem” —
/// <see cref="ActionFailedException"/> trafia do klienta jako <c>upstreamUnavailable</c>.
/// </summary>
public interface IComputerControl
{
    /// <summary>Stan „przed” do potwierdzenia i audytu.</summary>
    Task<string> DescribeAsync(string computerName, CancellationToken cancellationToken = default);

    Task<ActionChange> RestartAsync(string computerName, CancellationToken cancellationToken = default);

    Task<ActionChange> LockAsync(string computerName, CancellationToken cancellationToken = default);
}

/// <summary>Zapis do AD (M4): reset hasła i dodanie do grupy.</summary>
public interface IAdWriteService
{
    Task<string> DescribeUserAsync(string samAccountName, CancellationToken cancellationToken = default);

    /// <summary>Hasło tymczasowe generuje serwer; metoda nie zwraca go w opisie zmiany.</summary>
    Task<ActionChange> ResetPasswordAsync(
        string samAccountName,
        string temporaryPassword,
        CancellationToken cancellationToken = default);

    Task<ActionChange> AddToGroupAsync(
        string samAccountName,
        string groupName,
        CancellationToken cancellationToken = default);
}

/// <summary>Źródło odmówiło wykonania akcji (stacja, WinRM, LDAP). UI musi pokazać ten błąd.</summary>
public sealed class ActionFailedException : Exception
{
    public ActionFailedException(string sourceName, string message, Exception? inner = null)
        : base(message, inner)
    {
        SourceName = sourceName;
    }

    /// <summary>Nazwa źródła, które odmówiło (np. „Stacja KS-101-01”, „Active Directory”).</summary>
    public string SourceName { get; }
}

/// <summary>Cel akcji nie istnieje — klient dostaje <c>notFound</c>, nie błąd źródła.</summary>
public sealed class ActionTargetNotFoundException : Exception
{
    public ActionTargetNotFoundException(string message) : base(message)
    {
    }
}
