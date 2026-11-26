using System.Windows;

namespace ZseAdmin.Client;

public partial class PromptWindow : Window
{
    public PromptWindow(string title, string prompt)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Focus();
    }

    public string? Value { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Value = ValueBox.Text;
        DialogResult = true;
    }
}
