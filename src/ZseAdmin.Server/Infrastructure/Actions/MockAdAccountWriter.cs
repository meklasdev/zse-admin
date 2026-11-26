using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Actions;

/// <summary>Zapis do AD z danymi przykładowymi — niczego nie zmienia.</summary>
public sealed class MockAdAccountWriter : IAdAccountWriter
{
    public Task<AdWriteOutcome> ResetPasswordAsync(
        string samAccountName,
        string newPassword,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AdWriteOutcome.Ok(
            changed: true,
            before: "(hasło utajnione)",
            after: "(hasło zmienione)",
            message: $"Symulowany reset hasła dla '{samAccountName}' (dane przykładowe)."));

    public Task<AdWriteOutcome> AddUserToGroupAsync(
        string samAccountName,
        string groupName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AdWriteOutcome.Ok(
            changed: true,
            before: "brak na liście członków",
            after: $"członek grupy '{groupName}'",
            message: $"Symulowane dodanie '{samAccountName}' do grupy '{groupName}' (dane przykładowe)."));
}
