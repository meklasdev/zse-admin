using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Actions;

/// <summary>Akcje na stacjach z danymi przykładowymi — niczego nie wykonuje.</summary>
public sealed class MockComputerActionExecutor : IComputerActionExecutor
{
    public Task<ActionOutcome> RestartAsync(string computerName, CancellationToken cancellationToken = default) =>
        Task.FromResult(ActionOutcome.Ok($"Symulowany restart komputera {computerName} (dane przykładowe)."));

    public Task<ActionOutcome> LockAsync(string computerName, CancellationToken cancellationToken = default) =>
        Task.FromResult(ActionOutcome.Ok($"Symulowana blokada ekranu komputera {computerName} (dane przykładowe)."));
}
