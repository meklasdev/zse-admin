using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Windows;

/// <summary>
/// M4 — akcje na stacjach przez wbudowane narzędzia Windows:
/// restart = <c>shutdown.exe /r /m</c>, blokada ekranu = WinRM + <c>LockWorkStation</c>.
/// Wymagania i ograniczenia (WinRM, uprawnienia konta usługi) są opisane w ADR-0008.
/// Każdy niezerowy kod wyjścia to <see cref="ActionFailedException"/> — nigdy „sukces”.
/// </summary>
public sealed class WindowsComputerControl : IComputerControl
{
    private readonly IComputerDirectory _computers;
    private readonly IProcessRunner _runner;
    private readonly ActionOptions _options;
    private readonly IClock _clock;

    public WindowsComputerControl(
        IComputerDirectory computers,
        IProcessRunner runner,
        ActionOptions options,
        IClock clock)
    {
        _computers = computers;
        _runner = runner;
        _options = options;
        _clock = clock;
    }

    public async Task<string> DescribeAsync(
        string computerName,
        CancellationToken cancellationToken = default)
    {
        var computer = await FindAsync(computerName, cancellationToken).ConfigureAwait(false);

        return $"stan: {computer.Status}; IP: {computer.IpAddress ?? "brak danych"}; " +
               $"pracownia: {computer.ClassroomName ?? "poza pracownią"}; " +
               $"obserwacja: {computer.StatusObservedAtUtc:yyyy-MM-dd HH:mm} UTC";
    }

    public async Task<ActionChange> RestartAsync(
        string computerName,
        CancellationToken cancellationToken = default)
    {
        var before = await DescribeAsync(computerName, cancellationToken).ConfigureAwait(false);
        ValidateHostName(computerName);

        var delay = Math.Clamp(_options.RestartDelaySeconds, 0, 300);
        var result = await _runner.RunAsync(
                "shutdown.exe",
                BuildRestartArguments(computerName, delay),
                TimeSpan.FromSeconds(20),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new ActionFailedException(
                $"Stacja {computerName}",
                $"shutdown.exe zakończony kodem {result.ExitCode}: {Trim(result.Error, result.Output)}");
        }

        var after = $"restart zaplanowany za {delay} s ({_clock.UtcNow:yyyy-MM-dd HH:mm:ss} UTC)";
        return new ActionChange(
            before,
            after,
            $"Wysłano polecenie restartu do {computerName} (za {delay} s).");
    }

    public async Task<ActionChange> LockAsync(
        string computerName,
        CancellationToken cancellationToken = default)
    {
        var before = await DescribeAsync(computerName, cancellationToken).ConfigureAwait(false);
        ValidateHostName(computerName);

        var result = await _runner.RunAsync(
                "powershell.exe",
                BuildLockArguments(computerName),
                TimeSpan.FromSeconds(25),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new ActionFailedException(
                $"Stacja {computerName}",
                $"WinRM/LockWorkStation zakończony kodem {result.ExitCode}: {Trim(result.Error, result.Output)}");
        }

        var after = $"ekran zablokowany ({_clock.UtcNow:yyyy-MM-dd HH:mm:ss} UTC; WinRM)";
        return new ActionChange(
            before,
            after,
            $"Zablokowano ekran stacji {computerName} przez WinRM.");
    }

    /// <summary>Widoczne dla testów: budowa argumentów bez uruchamiania polecenia.</summary>
    public static string BuildRestartArguments(string computerName, int delaySeconds) =>
        $"/r /m \\\\{computerName} /t {Math.Clamp(delaySeconds, 0, 300)} /f /c \"ZSE-Admin restart\"";

    /// <summary>Widoczne dla testów: blokada przez PowerShell Remoting.</summary>
    public static string BuildLockArguments(string computerName) =>
        $"-NoProfile -NonInteractive -Command \"Invoke-Command -ComputerName '{computerName}' " +
        "-ScriptBlock { rundll32.exe user32.dll,LockWorkStation } -ErrorAction Stop\"";

    private async Task<ComputerDto> FindAsync(string computerName, CancellationToken cancellationToken)
    {
        var all = await _computers.ListAsync(null, cancellationToken).ConfigureAwait(false);
        var computer = all.Items.FirstOrDefault(c =>
            string.Equals(c.Name, computerName, StringComparison.OrdinalIgnoreCase));

        return computer ?? throw new ActionTargetNotFoundException(
            $"Nie znaleziono komputera '{computerName}' w inwentarzu.");
    }

    private static void ValidateHostName(string computerName)
    {
        if (string.IsNullOrWhiteSpace(computerName) ||
            computerName.Length > 255 ||
            !computerName.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'))
        {
            throw new ActionFailedException(
                "Stacja",
                "Nieprawidłowa nazwa stacji — odmowa zbudowania polecenia systemowego.");
        }
    }

    private static string Trim(string error, string output)
    {
        var text = string.IsNullOrWhiteSpace(error) ? output : error;
        text = text.Trim();
        return text.Length == 0 ? "brak szczegółów ze źródła" : text[..Math.Min(text.Length, 300)];
    }
}
