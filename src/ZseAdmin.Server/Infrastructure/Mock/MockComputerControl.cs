using System.Collections.Concurrent;
using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Mock;

/// <summary>
/// Akcje na stacjach w trybie danych przykładowych. Świadomie NIE udaje realnego
/// restartu — komunikat o makiecie jest częścią odpowiedzi, a audyt i tak zapisuje operację.
/// </summary>
public sealed class MockComputerControl : IComputerControl
{
    private readonly IComputerDirectory _computers;
    private readonly ActionOptions _options;
    private readonly ConcurrentDictionary<string, byte> _locked = new(StringComparer.OrdinalIgnoreCase);

    public MockComputerControl(IComputerDirectory computers, ActionOptions options)
    {
        _computers = computers;
        _options = options;
    }

    public async Task<string> DescribeAsync(
        string computerName,
        CancellationToken cancellationToken = default)
    {
        var computer = await FindAsync(computerName, cancellationToken).ConfigureAwait(false);
        var locked = _locked.ContainsKey(computer.Name) ? "tak" : "nie";

        return $"stan: {computer.Status}; IP: {computer.IpAddress ?? "brak danych"}; " +
               $"blokada: {locked}; obserwacja: {computer.StatusObservedAtUtc:yyyy-MM-dd HH:mm} UTC";
    }

    public async Task<ActionChange> RestartAsync(
        string computerName,
        CancellationToken cancellationToken = default)
    {
        var before = await DescribeAsync(computerName, cancellationToken).ConfigureAwait(false);
        var delay = Math.Clamp(_options.RestartDelaySeconds, 0, 300);
        var after = $"restart zaplanowany za {delay} s (makieta)";

        return new ActionChange(
            before,
            after,
            "Zaplanowano restart (dane przykładowe — stacja nie została fizycznie zrestartowana).");
    }

    public async Task<ActionChange> LockAsync(
        string computerName,
        CancellationToken cancellationToken = default)
    {
        var before = await DescribeAsync(computerName, cancellationToken).ConfigureAwait(false);
        _locked[computerName] = 1;
        var after = before.Replace("blokada: nie", "blokada: tak", StringComparison.Ordinal) + " · ekran zablokowany (makieta)";

        return new ActionChange(
            before,
            after,
            "Zablokowano ekran (dane przykładowe — polecenie nie zostało wysłane do stacji).");
    }

    private async Task<ComputerDto> FindAsync(string computerName, CancellationToken cancellationToken)
    {
        var all = await _computers.ListAsync(null, cancellationToken).ConfigureAwait(false);
        var computer = all.Items.FirstOrDefault(c =>
            string.Equals(c.Name, computerName, StringComparison.OrdinalIgnoreCase));

        return computer ?? throw new ActionTargetNotFoundException(
            $"Nie znaleziono komputera '{computerName}' w inwentarzu.");
    }
}
