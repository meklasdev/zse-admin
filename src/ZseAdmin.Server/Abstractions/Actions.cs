namespace ZseAdmin.Server.Abstractions;

/// <summary>
/// Wynik akcji na stacji. <see cref="Success"/> = false nigdy nie może zostać
/// przedstawiony klientowi jako „udało się” — router zamienia go na błąd źródła.
/// </summary>
public sealed record ActionOutcome(bool Success, string Message)
{
    public static ActionOutcome Ok(string message) => new(true, message);

    public static ActionOutcome Failed(string message) => new(false, message);
}

/// <summary>Restart/blokada stacji (M4). Implementacja produkcyjna na Windows.</summary>
public interface IComputerActionExecutor
{
    Task<ActionOutcome> RestartAsync(string computerName, CancellationToken cancellationToken = default);

    Task<ActionOutcome> LockAsync(string computerName, CancellationToken cancellationToken = default);
}

/// <summary>Rodzaj błędu zapisu do AD — router mapuje go na odpowiedni kod protokołu.</summary>
public enum AdWriteErrorKind
{
    None = 0,
    NotFound = 1,
    Rejected = 2,
    Unavailable = 3
}

/// <summary>
/// Wynik zapisu do AD. <see cref="Before"/>/<see cref="After"/> trafiają do audytu,
/// dlatego nie wolno tu umieszczać sekretów (hasło → „(hasło utajnione)”).
/// </summary>
public sealed record AdWriteOutcome(
    bool Success,
    AdWriteErrorKind ErrorKind,
    bool Changed,
    string Before,
    string After,
    string Message)
{
    public static AdWriteOutcome Ok(bool changed, string before, string after, string message) =>
        new(true, AdWriteErrorKind.None, changed, before, after, message);

    public static AdWriteOutcome Fail(AdWriteErrorKind kind, string message) =>
        new(false, kind, false, "(brak danych)", "(brak zmiany)", message);
}

/// <summary>Zapis do AD: reset hasła i dodawanie do grup (M4).</summary>
public interface IAdAccountWriter
{
    Task<AdWriteOutcome> ResetPasswordAsync(
        string samAccountName,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<AdWriteOutcome> AddUserToGroupAsync(
        string samAccountName,
        string groupName,
        CancellationToken cancellationToken = default);
}
