namespace ZseAdmin.Server.Abstractions;

/// <summary>
/// Port zapisu do AD przez LDAP (M4). Tylko dwie dozwolone operacje — brak dowolnych
/// modyfikacji atrybutów i brak endpointu przyjmującego dowolny DN/filtr.
/// </summary>
public interface ILdapDirectoryWriter
{
    /// <summary>
    /// Ustawia <c>unicodePwd</c> (wymaga LDAPS) i wymusza zmianę hasła przy następnym logowaniu.
    /// </summary>
    void ResetPassword(string userDistinguishedName, string temporaryPassword);

    /// <summary>Dodaje <c>member</c> do grupy (idempotentnie obsługiwane w warstwie wyżej).</summary>
    void AddToGroup(string groupDistinguishedName, string userDistinguishedName);
}
