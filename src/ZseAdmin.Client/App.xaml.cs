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

        _connection = new WsClientConnection(endpoint, settings.Token, settings.UseWindowsAuthentication);
        var api = new ZseAdminApiClient(_connection);
        var dispatcher = new DispatcherAdapter(Dispatcher);
        var interaction = new WpfUserInteraction();

        var viewModel = new MainViewModel(api, _connection, dispatcher, interaction);
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
