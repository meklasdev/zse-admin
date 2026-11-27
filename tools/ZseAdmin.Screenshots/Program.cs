using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using ZseAdmin.Client;
using ZseAdmin.Client.Core.Api;
using ZseAdmin.Client.Core.Connection;
using ZseAdmin.Client.Core.ViewModels;

namespace ZseAdmin.Screenshots;

/// <summary>
/// ZAD-11 — zrzuty ekranu z <b>realnego</b> klienta WPF (nie z makiety HTML), zasilanego danymi
/// z serwera w trybie <c>UseMockData = true</c>. Narzędzie uruchamia prawdziwy
/// <see cref="MainWindow"/>, czeka na <c>welcome</c> i dane, a następnie renderuje widok
/// w 100%, 125% i 150% DPI do plików PNG. Działa w CI na <c>windows-latest</c> (sesja 0),
/// bo nie wymaga widocznego okna.
/// </summary>
internal static class Program
{
    private const int WindowWidth = 1460;
    private const int WindowHeight = 800;

    /// <summary>Pusty zrzut 1460×800 kompresuje się do kilku kB — to kontrola jakości, nie estetyka.</summary>
    private const int MinimumPngBytes = 10_000;

    private static readonly (int Percent, double Scale)[] DpiScales =
    {
        (100, 1.0),
        (125, 1.25),
        (150, 1.5)
    };

    [STAThread]
    public static int Main(string[] args)
    {
        ScreenshotOptions options;
        try
        {
            options = ScreenshotOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var exitCode = 3;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        LoadApplicationResources(app);

        app.Startup += async (_, _) =>
        {
            try
            {
                exitCode = await CaptureAsync(options);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Nie udało się wykonać zrzutów: " + ex);
                exitCode = 3;
            }
            finally
            {
                app.Shutdown();
            }
        };

        app.Run();
        return exitCode;
    }

    /// <summary>
    /// Klient ładuje słowniki z App.xaml; tutaj tworzymy własną <see cref="Application"/>,
    /// więc rejestrujemy dokładnie te same zasoby (te same pliki BAML z biblioteki klienta).
    /// </summary>
    private static void LoadApplicationResources(Application app)
    {
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/ZseAdmin.Client;component/Styles/Theme.xaml",
                UriKind.Absolute)
        });

        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/ZseAdmin.Client;component/Styles/Controls.xaml",
                UriKind.Absolute)
        });

        // MainWindow.xaml wiąże widoczność przez konwerter z App.xaml klienta; w tym procesie
        // App.xaml nie jest uruchamiany, więc konwerter trzeba zarejestrować pod tą samą nazwą.
        app.Resources["BoolToVisibility"] = new BooleanToVisibilityConverter();
    }

    private static async Task<int> CaptureAsync(ScreenshotOptions options)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        await using var connection = new WsClientConnection(options.ServerUri, options.Token);
        var api = new ZseAdminApiClient(connection);
        var dispatcher = new DispatcherAdapter(Application.Current.Dispatcher);
        var viewModel = new MainViewModel(api, connection, dispatcher);

        Console.WriteLine($"Łączenie z {options.ServerUri} …");
        await viewModel.StartAsync();

        if (!await WaitForDataAsync(viewModel, options.WaitTimeout))
        {
            if (viewModel.IsAccessDenied)
            {
                Console.Error.WriteLine(AccessDenial.Message);
                return 4;
            }

            Console.Error.WriteLine(
                $"Serwer nie dostarczył danych w {options.WaitTimeout.TotalSeconds:0} s. " +
                $"Status: {viewModel.StatusMessage} · świeżość: {viewModel.FreshnessText}");
            return 2;
        }

        Console.WriteLine(
            $"Dane gotowe: {viewModel.Classrooms.Count} pracowni, {viewModel.Computers.Count} komputerów, " +
            $"{viewModel.Logins.Count} logowań · {viewModel.IdentityText} · {viewModel.FreshnessText}");

        var host = CreateHost(viewModel);
        var saved = new List<string>();

        foreach (var (percent, scale) in DpiScales)
        {
            saved.Add(UiSnapshot.Render(
                host,
                WindowWidth,
                WindowHeight,
                scale,
                Path.Combine(options.OutputDirectory, $"main-{percent}.png")));
        }

        var tabs = UiSnapshot.FindDescendant<TabControl>(host);
        if (tabs is null)
        {
            Console.Error.WriteLine("Nie znaleziono TabControl — zakładka akcji nie zostanie zrzucona.");
        }
        else
        {
            tabs.SelectedIndex = 1;
            await Dispatcher.Yield(DispatcherPriority.Background);
            host.UpdateLayout();

            foreach (var (percent, scale) in DpiScales)
            {
                saved.Add(UiSnapshot.Render(
                    host,
                    WindowWidth,
                    WindowHeight,
                    scale,
                    Path.Combine(options.OutputDirectory, $"akcje-i-audyt-{percent}.png")));
            }
        }

        var tooSmall = saved
            .Where(file => new FileInfo(file).Length < MinimumPngBytes)
            .ToList();

        if (tooSmall.Count > 0)
        {
            Console.Error.WriteLine(
                "Podejrzanie małe pliki (możliwy pusty zrzut): " + string.Join(", ", tooSmall));
            return 5;
        }

        foreach (var file in saved)
        {
            Console.WriteLine($"Zapisano {file} ({new FileInfo(file).Length / 1024} kB).");
        }

        return 0;
    }

    /// <summary>
    /// Bierze zawartość prawdziwego <see cref="MainWindow"/> (ten sam XAML, style i bindingi)
    /// i wkłada ją do kontenera o stałym rozmiarze. Okno nie jest pokazywane — w CI nie ma pulpitu.
    /// </summary>
    private static Border CreateHost(MainViewModel viewModel)
    {
        var window = new MainWindow { DataContext = viewModel };

        if (window.Content is not FrameworkElement content)
        {
            throw new InvalidOperationException("MainWindow nie ma zawartości, której można użyć do zrzutu.");
        }

        window.Content = null;
        content.DataContext = viewModel;

        var host = new Border
        {
            Child = content,
            Width = WindowWidth,
            Height = WindowHeight,
            Background = window.Background,
            UseLayoutRounding = true
        };

        TextElement.SetFontFamily(host, window.FontFamily);
        host.SetValue(TextElement.ForegroundProperty, window.Foreground);

        return host;
    }

    /// <summary>
    /// Czeka na pierwszy pełny stan: pracownie, komputery i świeżość z serwera. Samo
    /// <c>welcome</c> nie wystarcza — zrzut musi pokazywać dane, nie „Pobieranie danych…”.
    /// </summary>
    private static async Task<bool> WaitForDataAsync(MainViewModel viewModel, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (viewModel.IsAccessDenied)
            {
                return false;
            }

            if (viewModel.Classrooms.Count > 0 &&
                viewModel.Computers.Count > 0 &&
                viewModel.FreshnessText.StartsWith("Dane", StringComparison.Ordinal))
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }
}
