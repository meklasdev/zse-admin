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

/// <summary>Rodzaj akcji wykonywanej na stacji (etap M4).</summary>
public enum ComputerAction
{
    /// <summary>Wymuszony restart (np. <c>shutdown.exe /r /f /t 0</c>).</summary>
    Restart = 1,

    /// <summary>Blokada ekranu w aktywnej sesji konsolowej.</summary>
    Lock = 2
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
