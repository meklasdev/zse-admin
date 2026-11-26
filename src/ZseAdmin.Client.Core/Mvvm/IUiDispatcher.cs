namespace ZseAdmin.Client.Core.Mvvm;

/// <summary>
/// Abstrakcja wątku UI. Warstwa Core nie zna WPF, więc testy i przyszły klient
/// (np. Avalonia) mogą dostarczyć własną implementację.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Wykonuje akcję asynchronicznie na wątku UI (bez oczekiwania).</summary>
    void Post(Action action);

    /// <summary>Wykonuje akcję na wątku UI i pozwala poczekać na jej zakończenie.</summary>
    Task InvokeAsync(Action action);
}
