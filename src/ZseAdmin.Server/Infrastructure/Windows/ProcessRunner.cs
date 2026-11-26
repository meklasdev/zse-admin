using System.Diagnostics;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Windows;

/// <summary>Uruchamia krótkie polecenie systemowe z budżetem czasu i przechwyconym wyjściem.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new ActionFailedException("System", $"Nie udało się uruchomić '{fileName}'.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(timeout);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        string output;
        string error;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            output = await stdoutTask.ConfigureAwait(false);
            error = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Proces już się zakończył.
            }

            throw new ActionFailedException(
                "System",
                $"Polecenie '{fileName}' przekroczyło budżet {timeout.TotalSeconds:0} s.");
        }

        return new ProcessResult(process.ExitCode, output, error);
    }
}
