namespace ZseAdmin.Server.Abstractions;

/// <summary>Wynik uruchomienia polecenia systemowego na serwerze (M4).</summary>
public sealed record ProcessResult(int ExitCode, string Output, string Error);

/// <summary>
/// Port uruchamiania komend Windows. Wydzielony, żeby akcje na stacjach były testowalne
/// bez faktycznego restartowania komputerów.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
