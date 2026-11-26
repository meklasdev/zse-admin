using System.Windows;
using ZseAdmin.Client.Core.Mvvm;

namespace ZseAdmin.Client;

/// <summary>Implementacja interakcji z użytkownikiem oparta na dialogach WPF.</summary>
public sealed class WpfUserInteraction : IUserInteraction
{
    public bool AskConfirmation(string title, string message) =>
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public string? AskText(string title, string prompt)
    {
        var dialog = new PromptWindow(title, prompt) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public string? AskNewPassword(string title, string prompt)
    {
        var dialog = new ResetPasswordWindow(title, prompt) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Password : null;
    }
}
