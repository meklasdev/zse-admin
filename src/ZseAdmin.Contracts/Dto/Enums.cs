namespace ZseAdmin.Contracts;

/// <summary>Stan dostępności komputera w sieci. Brak danych nie jest tym samym co offline.</summary>
public enum ComputerStatus
{
    /// <summary>Nie wiadomo — brak udanego sprawdzenia lub dane wygasły.</summary>
    Unknown = 0,

    /// <summary>Komputer odpowiada w sieci.</summary>
    Online = 1,

    /// <summary>Ostatnie sprawdzenie nie uzyskało odpowiedzi.</summary>
    Offline = 2
}

/// <summary>Rodzaj zdarzenia logowania zarejestrowanego na stacji.</summary>
public enum LoginEventType
{
    Logon = 1,
    Logoff = 2,
    FailedLogon = 3
}

/// <summary>Kompletność danych zwróconych przez źródło.</summary>
public enum DataCompleteness
{
    /// <summary>Źródło potwierdziło, że zwróciło wszystkie dane w zakresie zapytania.</summary>
    Complete = 0,

    /// <summary>Dane są niepełne (np. log access denied na części stacji, obcięty zakres czasu).</summary>
    Partial = 1,

    /// <summary>Źródło nie było dostępne — brak danych nie oznacza braku zdarzeń.</summary>
    Unavailable = 2
}

/// <summary>
/// Operacja zmieniająca stan (etap M4). Każda wymaga potwierdzenia i wpisu audytowego.
/// </summary>
public enum AdminActionKind
{
    /// <summary>Restart stacji (<c>computers.control</c>).</summary>
    RestartComputer = 1,

    /// <summary>Zablokowanie ekranu stacji (<c>computers.control</c>).</summary>
    LockComputer = 2,

    /// <summary>Reset hasła konta AD; hasło tymczasowe generuje serwer (<c>users.write</c>).</summary>
    ResetPassword = 3,

    /// <summary>Dodanie konta do grupy AD (<c>users.write</c>).</summary>
    AddToGroup = 4
}
