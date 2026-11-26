namespace ZseAdmin.Client.Core.Mvvm;

/// <summary>
/// Interakcje z użytkownikiem wymagane przez akcje (M4): potwierdzenia i pytania.
/// Interfejs żyje w Core (bez WPF), implementacja WPF — w warstwie klienta.
/// Testy podstawiają zamiennik.
/// </summary>
public interface IUserInteraction
{
    /// <summary>Pytanie tak/nie (potwierdzenie akcji destrukcyjnej).</summary>
    bool AskConfirmation(string title, string message);

    /// <summary>Pytanie o tekst jawny; <c>null</c> = anulowano.</summary>
    string? AskText(string title, string prompt);

    /// <summary>Pytanie o nowe hasło (pola ukryte + walidacja po stronie dialogu); <c>null</c> = anulowano.</summary>
    string? AskNewPassword(string title, string prompt);
}
