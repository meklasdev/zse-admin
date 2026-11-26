using System.DirectoryServices.Protocols;
using System.Text;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Ldap;

/// <summary>
/// M4 — dwie dozwolone modyfikacje AD. <c>unicodePwd</c> wymaga LDAPS i kodowania
/// UTF-16LE w cudzysłowach; razem z <c>pwdLastSet = 0</c> wymusza zmianę hasła
/// przy najbliższym logowaniu. Każdy błąd LDAP jest raportowany jako błąd źródła.
/// </summary>
public sealed class LdapDirectoryWriter : ILdapDirectoryWriter, IDisposable
{
    private readonly Lazy<LdapConnection> _connection;

    public LdapDirectoryWriter(LdapConnectionFactory factory)
    {
        _connection = new Lazy<LdapConnection>(factory.Create, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public void ResetPassword(string userDistinguishedName, string temporaryPassword)
    {
        var password = new DirectoryAttributeModification
        {
            Name = "unicodePwd",
            Operation = DirectoryAttributeOperation.Replace
        };
        password.Add(Encoding.Unicode.GetBytes($"\"{temporaryPassword}\""));

        var mustChange = new DirectoryAttributeModification
        {
            Name = "pwdLastSet",
            Operation = DirectoryAttributeOperation.Replace
        };
        mustChange.Add("0");

        Send(new ModifyRequest(userDistinguishedName, password, mustChange));
    }

    public void AddToGroup(string groupDistinguishedName, string userDistinguishedName)
    {
        var member = new DirectoryAttributeModification
        {
            Name = "member",
            Operation = DirectoryAttributeOperation.Add
        };
        member.Add(userDistinguishedName);

        Send(new ModifyRequest(groupDistinguishedName, member));
    }

    private void Send(ModifyRequest request)
    {
        try
        {
            var response = (ModifyResponse)_connection.Value.SendRequest(request);
            if (response.ResultCode != ResultCode.Success)
            {
                throw new ActionFailedException(
                    "Active Directory",
                    $"LDAP {response.ResultCode}: {response.ErrorMessage}");
            }
        }
        catch (Exception ex) when (ex is LdapException
                                       or DirectoryOperationException
                                       or InvalidOperationException
                                       or UnauthorizedAccessException)
        {
            throw new ActionFailedException("Active Directory", ex.Message, ex);
        }
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            _connection.Value.Dispose();
        }
    }
}
