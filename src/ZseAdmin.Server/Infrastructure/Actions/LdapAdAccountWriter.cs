using System.Text;
using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Ldap;

namespace ZseAdmin.Server.Infrastructure.Actions;

/// <summary>
/// Zapis do AD przez LDAP (M4): reset hasła (<c>unicodePwd</c>, wymaga LDAPS — AD
/// odrzuca zmianę po nieszyfrowanym kanale) i dodawanie do grup (<c>member</c>).
/// Wartości zwrócone do audytu nigdy nie zawierają hasła (przed/po = „utajnione”).
/// </summary>
public sealed class LdapAdAccountWriter : IAdAccountWriter
{
    private static readonly string[] UserAttributes = { "distinguishedName" };
    private static readonly string[] GroupAttributes = { "distinguishedName", "cn", "member" };

    private readonly ILdapDirectoryReader _ldap;
    private readonly ILdapWriter _writer;
    private readonly DirectoryOptions _options;

    public LdapAdAccountWriter(ILdapDirectoryReader ldap, ILdapWriter writer, DirectoryOptions options)
    {
        _ldap = ldap;
        _writer = writer;
        _options = options;
    }

    public Task<AdWriteOutcome> ResetPasswordAsync(
        string samAccountName,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (!_options.UseSsl)
        {
            return Task.FromResult(AdWriteOutcome.Fail(
                AdWriteErrorKind.Unavailable,
                "Zmiana hasła wymaga LDAPS (ZseAdmin:Directory:UseSsl=true). " +
                "AD odrzuca atrybut unicodePwd po nieszyfrowanym kanale."));
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            return Task.FromResult(AdWriteOutcome.Fail(
                AdWriteErrorKind.Rejected,
                "Hasło musi mieć co najmniej 8 znaków."));
        }

        var searchBase = ResolveSearchBase();
        var escaped = LdapInventoryReader.EscapeFilterValue(samAccountName);
        var entries = _ldap.Search(
            searchBase,
            $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={escaped}))",
            UserAttributes,
            cancellationToken);

        var user = entries.FirstOrDefault();
        if (user is null)
        {
            return Task.FromResult(AdWriteOutcome.Fail(
                AdWriteErrorKind.NotFound,
                $"Nie znaleziono konta '{samAccountName}'."));
        }

        // unicodePwd: wartość to hasło w cudzysłowie, zakodowane UTF-16LE.
        // Nigdy nie logujemy i nie zapisujemy tej wartości do audytu.
        var passwordBytes = Encoding.Unicode.GetBytes($"\"{newPassword}\"");

        try
        {
            _writer.Modify(
                user.DistinguishedName,
                new LdapModification("unicodePwd", LdapModificationOperation.Replace, new[] { passwordBytes }),
                cancellationToken);
        }
        catch (SourceUnavailableException ex)
        {
            return Task.FromResult(AdWriteOutcome.Fail(
                AdWriteErrorKind.Rejected,
                ScrubPassword($"AD odrzuciło zmianę hasła (uprawnienia lub polityka haseł): {ex.Message}", newPassword)));
        }

        return Task.FromResult(AdWriteOutcome.Ok(
            changed: true,
            before: "(hasło utajnione)",
            after: "(hasło zmienione)",
            message: $"Hasło zmienione dla '{samAccountName}'."));
    }

    public Task<AdWriteOutcome> AddUserToGroupAsync(
        string samAccountName,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        var searchBase = ResolveSearchBase();
        var escapedUser = LdapInventoryReader.EscapeFilterValue(samAccountName);
        var escapedGroup = LdapInventoryReader.EscapeFilterValue(groupName);

        var userEntries = _ldap.Search(
            searchBase,
            $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={escapedUser}))",
            UserAttributes,
            cancellationToken);

        var user = userEntries.FirstOrDefault();
        if (user is null)
        {
            return Task.FromResult(AdWriteOutcome.Fail(
                AdWriteErrorKind.NotFound,
                $"Nie znaleziono konta '{samAccountName}'."));
        }

        var groupEntries = _ldap.Search(
            searchBase,
            $"(&(objectCategory=group)(|(sAMAccountName={escapedGroup})(cn={escapedGroup})))",
            GroupAttributes,
            cancellationToken);

        var group = groupEntries.FirstOrDefault();
        if (group is null)
        {
            return Task.FromResult(AdWriteOutcome.Fail(
                AdWriteErrorKind.NotFound,
                $"Nie znaleziono grupy '{groupName}'."));
        }

        var normalizedUserDn = NormalizeDn(user.DistinguishedName);
        var members = group.Texts("member");
        var alreadyMember = members.Any(member =>
            string.Equals(NormalizeDn(member), normalizedUserDn, StringComparison.OrdinalIgnoreCase));

        if (alreadyMember)
        {
            return Task.FromResult(AdWriteOutcome.Ok(
                changed: false,
                before: $"członek grupy '{groupName}'",
                after: $"członek grupy '{groupName}'",
                message: $"Konto '{samAccountName}' już należy do grupy '{groupName}'."));
        }

        try
        {
            _writer.Modify(
                group.DistinguishedName,
                new LdapModification(
                    "member",
                    LdapModificationOperation.Add,
                    new[] { Encoding.UTF8.GetBytes(user.DistinguishedName) }),
                cancellationToken);
        }
        catch (SourceUnavailableException ex)
        {
            return Task.FromResult(AdWriteOutcome.Fail(
                AdWriteErrorKind.Rejected,
                $"AD odrzuciło dodanie do grupy (uprawnienia): {ex.Message}"));
        }

        return Task.FromResult(AdWriteOutcome.Ok(
            changed: true,
            before: "brak na liście członków",
            after: $"członek grupy '{groupName}'",
            message: $"Dodano '{samAccountName}' do grupy '{groupName}'."));
    }

    private string ResolveSearchBase()
    {
        if (!string.IsNullOrWhiteSpace(_options.SearchBase))
        {
            return _options.SearchBase!.Trim();
        }

        var domain = _options.Domain;
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new SourceUnavailableException(
                "Active Directory",
                "Brak konfiguracji LDAP: ustaw 'ZseAdmin:Directory:SearchBase' albo 'ZseAdmin:Directory:Domain'.");
        }

        return LdapDirectoryReader.DomainToSearchBase(domain);
    }

    private static string NormalizeDn(string distinguishedName) =>
        string.Join(",", distinguishedName.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()));

    private static string ScrubPassword(string message, string password) =>
        string.IsNullOrEmpty(password)
            ? message
            : message.Replace(password, "***", StringComparison.Ordinal);
}
