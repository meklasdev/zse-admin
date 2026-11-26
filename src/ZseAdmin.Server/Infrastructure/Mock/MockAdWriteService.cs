using System.Collections.Concurrent;
using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Mock;

/// <summary>
/// Zapis do AD w trybie danych przykładowych. Zachowuje grupy w pamięci (żeby „przed/po”
/// miało sens w UI i audycie), ale nie dotyka katalogu. Hasła nie są nigdy zapisywane.
/// </summary>
public sealed class MockAdWriteService : IAdWriteService
{
    private readonly IAdDirectory _users;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<string, HashSet<string>> _extraGroups =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _passwordResets =
        new(StringComparer.OrdinalIgnoreCase);

    public MockAdWriteService(IAdDirectory users, IClock clock)
    {
        _users = users;
        _clock = clock;
    }

    public async Task<string> DescribeUserAsync(
        string samAccountName,
        CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(samAccountName, cancellationToken).ConfigureAwait(false);
        return Describe(user);
    }

    public async Task<ActionChange> ResetPasswordAsync(
        string samAccountName,
        string temporaryPassword,
        CancellationToken cancellationToken = default)
    {
        _ = temporaryPassword; // celowo: hasło nie trafia do opisu zmiany ani audytu
        var user = await FindAsync(samAccountName, cancellationToken).ConfigureAwait(false);

        _passwordResets[user.SamAccountName] = _clock.UtcNow;
        var before = $"hasło: ***; ostatni reset: {PreviousReset(user.SamAccountName)}";
        var after = "hasło: *** (tymczasowe, makieta); pwdLastSet: 0 (wymagana zmiana)";

        return new ActionChange(
            before,
            after,
            "Ustawiono hasło tymczasowe (dane przykładowe — AD nie zostało zmienione).");
    }

    public async Task<ActionChange> AddToGroupAsync(
        string samAccountName,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        var user = await FindAsync(samAccountName, cancellationToken).ConfigureAwait(false);
        var before = Describe(user);

        var groups = _extraGroups.GetOrAdd(
            user.SamAccountName,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var added = groups.Add(groupName.Trim());
        var after = $"grupy: {string.Join(", ", AllGroups(user).OrderBy(g => g, StringComparer.OrdinalIgnoreCase))}";

        return new ActionChange(
            before,
            after,
            added
                ? $"Dodano konto do grupy '{groupName}' (dane przykładowe — AD nie zostało zmienione)."
                : $"Konto już należało do grupy '{groupName}' — nie wykonano zmiany.");
    }

    private string PreviousReset(string sam) =>
        _passwordResets.TryGetValue(sam, out var at)
            ? $"{at:yyyy-MM-dd HH:mm} UTC"
            : "brak";

    private string Describe(AdUserDto user) =>
        $"grupy: {string.Join(", ", AllGroups(user).OrderBy(g => g, StringComparer.OrdinalIgnoreCase))}; " +
        $"konto włączone: {(user.Enabled ? "tak" : "nie")}";

    private IEnumerable<string> AllGroups(AdUserDto user) =>
        user.Groups.Concat(
            _extraGroups.TryGetValue(user.SamAccountName, out var extra)
                ? (IEnumerable<string>)extra
                : Array.Empty<string>());

    private async Task<AdUserDto> FindAsync(string samAccountName, CancellationToken cancellationToken)
    {
        var result = await _users.FindUserAsync(samAccountName, cancellationToken).ConfigureAwait(false);
        return result.User ?? throw new ActionTargetNotFoundException(
            $"Nie znaleziono konta '{samAccountName}' w katalogu.");
    }
}
