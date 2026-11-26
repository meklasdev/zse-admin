namespace ZseAdmin.Server.Abstractions;

public enum LdapModificationOperation
{
    Add = 0,
    Replace = 1,
    Delete = 2
}

/// <summary>Pojedyncza modyfikacja atrybutu LDAP.</summary>
public sealed record LdapModification(
    string Attribute,
    LdapModificationOperation Operation,
    IReadOnlyList<byte[]> Values);

/// <summary>
/// Minimalny port zapisu LDAP. Odczyt żyje w <see cref="ILdapDirectoryReader"/>;
/// ten port istnieje po to, żeby zapis dało się mockować i testować bez DC.
/// </summary>
public interface ILdapWriter
{
    /// <summary>Wykonuje modyfikację DN. Błędy LDAP rzuca jako <see cref="SourceUnavailableException"/>.</summary>
    void Modify(
        string distinguishedName,
        LdapModification modification,
        CancellationToken cancellationToken = default);
}
