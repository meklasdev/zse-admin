using System.Windows;
using ZseAdmin.Client.Core.Api;
using ZseAdmin.Client.Core.Connection;
using ZseAdmin.Client.Core.ViewModels;

namespace ZseAdmin.Client;

public partial class App : Application
{
    private WsClientConnection? _connection;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = AppSettings.Load();

        if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var endpoint))
        {
            MessageBox.Show(
                $"Nieprawidłowy adres serwera w appsettings.json: '{settings.ServerUrl}'.",
                "ZSE-Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
            return;
        }

        // M3: produkcja = Negotiate/Kerberos (brak tokenu, UseDefaultCredentials w kliencie WS).
        if (settings.UseWindowsAuth &&
            !string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Uwierzytelnianie Windows wymaga adresu wss:// (HTTPS). " +
                "Połączenie ws:// zostałoby odrzucone przez serwer produkcyjny.",
                "ZSE-Admin",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
            return;
        }

        var token = settings.UseWindowsAuth ? null : settings.Token;
        _connection = new WsClientConnection(endpoint, token);
        var api = new ZseAdminApiClient(_connection);
        var dispatcher = new DispatcherAdapter(Dispatcher);

        var viewModel = new MainViewModel(api, _connection, dispatcher);
        var window = new MainWindow { DataContext = viewModel };

        MainWindow = window;
        window.Show();

        _ = viewModel.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _connection?.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Zamykanie aplikacji nie może rzucić wyjątku do debuggera.
        }

        base.OnExit(e);
    }
}
