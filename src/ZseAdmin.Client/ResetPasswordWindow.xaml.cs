using System.Windows;

namespace ZseAdmin.Client;

public partial class ResetPasswordWindow : Window
{
    private const int MinPasswordLength = 8;

    public ResetPasswordWindow(string title, string prompt)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        PasswordBox.Focus();
    }

    public string? Password { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        var confirm = ConfirmBox.Password;

        if (password.Length < MinPasswordLength)
        {
            ValidationText.Text = $"Hasło musi mieć co najmniej {MinPasswordLength} znaków.";
            return;
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            ValidationText.Text = "Hasła nie są identyczne.";
            return;
        }

        Password = password;
        DialogResult = true;
    }
}
