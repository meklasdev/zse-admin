using System.Globalization;

namespace ZseAdmin.Screenshots;

/// <summary>Parametry uruchomienia narzędzia (bez zależności od biblioteki CLI).</summary>
internal sealed record ScreenshotOptions
{
    public const string DefaultServer = "ws://127.0.0.1:5080/ws";
    public const string DefaultToken = "dev-admin-token";
    public const string DefaultOutput = "screenshots";

    public required Uri ServerUri { get; init; }

    public string? Token { get; init; }

    public required string OutputDirectory { get; init; }

    public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromSeconds(90);

    public static ScreenshotOptions Parse(string[] args)
    {
        var server = DefaultServer;
        var token = DefaultToken;
        var output = DefaultOutput;
        var wait = TimeSpan.FromSeconds(90);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--server":
                    server = Next(args, ref i, "--server");
                    break;

                case "--token":
                    token = Next(args, ref i, "--token");
                    break;

                case "--out":
                    output = Next(args, ref i, "--out");
                    break;

                case "--wait":
                    var seconds = Next(args, ref i, "--wait");
                    if (!int.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
                        parsed <= 0)
                    {
                        throw new ArgumentException($"Nieprawidłowa wartość --wait: '{seconds}'.");
                    }

                    wait = TimeSpan.FromSeconds(parsed);
                    break;

                case "--help":
                case "-h":
                    throw new ArgumentException(Usage);

                default:
                    throw new ArgumentException($"Nieznany argument: '{args[i]}'.\n\n{Usage}");
            }
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var serverUri) ||
            (serverUri.Scheme != "ws" && serverUri.Scheme != "wss"))
        {
            throw new ArgumentException($"Adres serwera musi być ws:// lub wss:// (otrzymano '{server}').");
        }

        return new ScreenshotOptions
        {
            ServerUri = serverUri,
            Token = string.IsNullOrWhiteSpace(token) ? null : token,
            OutputDirectory = output,
            WaitTimeout = wait
        };
    }

    public static string Usage =>
        """
        Zrzuty UI z realnego klienta WPF (dane z serwera w trybie UseMockData).

          dotnet run --project tools/ZseAdmin.Screenshots -c Release -- [opcje]

          --server <ws://host:port/ws>   adres serwera (domyślnie ws://127.0.0.1:5080/ws)
          --token <token>                token deweloperski (domyślnie dev-admin-token)
          --out <katalog>                katalog na pliki PNG (domyślnie screenshots)
          --wait <sekundy>               maksymalny czas oczekiwania na dane (domyślnie 90)
          --help                         ta pomoc

        Kody wyjścia: 0 = zrzuty zapisane, 2 = brak danych w czasie --wait,
        3 = błąd renderowania, 4 = serwer odmówił dostępu, 5 = pusty zrzut (kontrola jakości).
        """;

    private static string Next(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Brak wartości dla {name}.\n\n{Usage}");
        }

        index++;
        return args[index];
    }
}
