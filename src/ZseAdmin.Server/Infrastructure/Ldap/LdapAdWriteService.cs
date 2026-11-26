using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Infrastructure.Ldap;

/// <summary>
/// M4 — zapis do AD: reset hasła i dodanie do grupy. Wyszukiwania używają tego samego
/// portu LDAP co M1 (filtry z escapingiem, bez dowolnych zapytań od klienta).
/// Hasło tymczasowe nie jest zapisywane w opisie zmiany ani w audycie.
/// </summary>
public sealed class LdapAdWriteService : IAdWriteService
{
    private readonly IAdInventoryReader _inventory;
    private readonly ILdapDirectoryReader _ldap;
    private readonly ILdapDirectoryWriter _writer;
    private readonly DirectoryOptions _options;

    public LdapAdWriteService(
        IAdInventoryReader inventory,
        ILdapDirectoryReader ldap,
        ILdapDirectoryWriter writer,
        DirectoryOptions options)
    {
        _inventory = inventory;
        _ldap = ldap;
        _writer = writer;
        _options = options;
    }

    public async Task<string> DescribeUserAsync(
        string samAccountName,
        CancellationToken cancellationToken = default)
    {
        var user = await _inventory.FindUserAsync(samAccountName, cancellationToken).ConfigureAwait(false)
            ?? throw new ActionTargetNotFoundException(
                $"Nie znaleziono konta '{samAccountName}' w Active Directory.");

        return Describe(user.Groups, user.Enabled);
    }

    public async Task<ActionChange> ResetPasswordAsync(
        string samAccountName,
        string temporaryPassword,
        CancellationToken cancellationToken = default)
    {
        _ = temporaryPassword; // przekazywane wyłącznie do bezpiecznego adaptera LDAP
        var before = (await DescribeUserAsync(samAccountName, cancellationToken).ConfigureAwait(false)) + "; hasło: ***";
        var userDn = FindDistinguishedName(
            $"(&(objectClass=user)(sAMAccountName={LdapInventoryReader.EscapeFilterValue(samAccountName)}))",
            $"Nie znaleziono konta '{samAccountName}' w Active Directory.",
            cancellationToken);

        _writer.ResetPassword(userDn, temporaryPassword);

        var after = before + " (tymczasowe); pwdLastSet: 0 (wymagana zmiana)";
        return new ActionChange(
            before,
            after,
            "Ustawiono hasło tymczasowe w AD; użytkownik musi je zmienić przy najbliższym logowaniu.");
    }

    public async Task<ActionChange> AddToGroupAsync(
        string samAccountName,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        var userDto = await _inventory.FindUserAsync(samAccountName, cancellationToken).ConfigureAwait(false)
            ?? throw new ActionTargetNotFoundException(
                $"Nie znaleziono konta '{samAccountName}' w Active Directory.");

        var before = Describe(userDto.Groups, userDto.Enabled);

        if (userDto.Groups.Any(g =>
                string.Equals(g, groupName, StringComparison.OrdinalIgnoreCase)))
        {
            return new ActionChange(
                before,
                before,
                $"Konto już należy do grupy '{groupName}' — nie wykonano zmiany.");
        }

        var userDn = FindDistinguishedName(
            $"(&(objectClass=user)(sAMAccountName={LdapInventoryReader.EscapeFilterValue(samAccountName)}))",
            $"Nie znaleziono konta '{samAccountName}' w Active Directory.",
            cancellationToken);

        var groupDn = FindDistinguishedName(
            $"(&(objectClass=group)(cn={LdapInventoryReader.EscapeFilterValue(groupName)}))",
            $"Nie znaleziono grupy '{groupName}' w Active Directory.",
            cancellationToken);

        _writer.AddToGroup(groupDn, userDn);

        var groups = userDto.Groups.Concat(new[] { groupName });
        var after = Describe(groups, userDto.Enabled);

        return new ActionChange(
            before,
            after,
            $"Dodano konto '{samAccountName}' do grupy '{groupName}' w AD.");
    }

    private string FindDistinguishedName(
        string filter,
        string notFoundMessage,
        CancellationToken cancellationToken)
    {
        var entries = _ldap.Search(
            SearchBase(),
            filter,
            new[] { "distinguishedName" },
            cancellationToken);

        var found = entries.FirstOrDefault();
        return found is null
            ? throw new ActionTargetNotFoundException(notFoundMessage)
            : found.DistinguishedName;
    }

    private string SearchBase() =>
        !string.IsNullOrWhiteSpace(_options.SearchBase)
            ? _options.SearchBase!.Trim()
            : LdapDirectoryReader.DomainToSearchBase(_options.Domain);

    private static string Describe(IEnumerable<string> groups, bool enabled) =>
        $"grupy: {string.Join(", ", groups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase))}; " +
        $"konto włączone: {(enabled ? "tak" : "nie")}";
}
