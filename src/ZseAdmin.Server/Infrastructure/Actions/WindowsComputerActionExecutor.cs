using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Actions;

/// <summary>
/// Akcje na stacjach (M4) z użyciem wbudowanych narzędzi Windows:
/// <list type="bullet">
/// <item><c>restart</c> → <c>shutdown.exe /r /m \\\\STACJA /f /t 0</c> (kod 0 = zlecono);</item>
/// <item><c>lock</c> → brak wbudowanej metody zdalnej blokady w Windows;
/// wymagany skonfigurowany <c>ZseAdmin:Actions:PsExecPath</c>
/// (<c>psexec \\\\STACJA -i rundll32 user32.dll,LockWorkStation</c>).</item>
/// </list>
/// Każda nieudana akcja zwraca <see cref="ActionOutcome.Failed"/> z komunikatem źródła —
/// nigdy udawany sukces.
/// </summary>
public sealed class WindowsComputerActionExecutor : IComputerActionExecutor
{
    private static readonly Regex ComputerNamePattern =
        new("^[A-Za-z0-9]([A-Za-z0-9-]{0,13}[A-Za-z0-9])?$", RegexOptions.Compiled);

    private readonly ActionsOptions _options;
    private readonly ILogger<WindowsComputerActionExecutor> _logger;

    public WindowsComputerActionExecutor(ActionsOptions options, ILogger<WindowsComputerActionExecutor> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<ActionOutcome> RestartAsync(string computerName, CancellationToken cancellationToken = default)
    {
        if (!ComputerNamePattern.IsMatch(computerName))
        {
            return ActionOutcome.Failed($"Nieprawidłowa nazwa komputera: '{computerName}'.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return ActionOutcome.Failed("Restart zdalny (shutdown.exe) jest dostępny wyłącznie na Windows.");
        }

        return await RunToolAsync(
            "shutdown.exe",
            new[] { "/r", "/m", $"\\\\{computerName}", "/f", "/t", "0", "/c", "ZSE-Admin: restart zlecony przez administratora" },
            $"Restart komputera {computerName} został zlecony.",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActionOutcome> LockAsync(string computerName, CancellationToken cancellationToken = default)
    {
        if (!ComputerNamePattern.IsMatch(computerName))
        {
            return ActionOutcome.Failed($"Nieprawidłowa nazwa komputera: '{computerName}'.");
        }

        var psexec = _options.PsExecPath;
        if (string.IsNullOrWhiteSpace(psexec))
        {
            return ActionOutcome.Failed(
                "Blokada zdalna wymaga skonfigurowanego PsExec (ZseAdmin:Actions:PsExecPath) — " +
                "Windows nie ma wbudowanej, obsługiwanej metody zablokowania sesji zdalnej. " +
                "Alternatywa: wygaszacz/lock wymuszony GPO.");
        }

        if (!File.Exists(psexec))
        {
            return ActionOutcome.Failed($"Skonfigurowany PsExecPath nie istnieje: '{psexec}'.");
        }

        return await RunToolAsync(
            psexec,
            new[] { $"\\\\{computerName}", "-i", "-accepteula", "rundll32.exe", "user32.dll,LockWorkStation" },
            $"Ekran komputera {computerName} zablokowany (PsExec, sesja konsolowa).",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActionOutcome> RunToolAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.ShutdownTimeoutSeconds, 1, 120));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return ActionOutcome.Failed($"Nie można uruchomić '{fileName}'.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var detail = (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim();

            if (process.ExitCode == 0)
            {
                return ActionOutcome.Ok(string.IsNullOrWhiteSpace(detail) ? successMessage : $"{successMessage} ({detail})");
            }

            _logger.LogWarning(
                "Narzędzie {Tool} na komputerze zakończyło się kodem {ExitCode}: {Detail}",
                fileName,
                process.ExitCode,
                detail);

            return ActionOutcome.Failed(
                $"'{fileName}' zakończyło się kodem {process.ExitCode}: {Truncate(detail, 400)}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ActionOutcome.Failed($"Przekroczono limit czasu ({timeout.TotalSeconds:0} s) narzędzia '{fileName}'.");
        }
        catch (Win32Exception ex)
        {
            return ActionOutcome.Failed($"Nie można uruchomić '{fileName}': {ex.Message}");
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
