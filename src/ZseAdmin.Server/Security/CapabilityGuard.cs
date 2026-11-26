using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Realtime;

namespace ZseAdmin.Server.Security;

public static class CapabilityGuard
{
    /// <summary>Rzuca <see cref="ProtocolException"/> z kodem <see cref="ErrorCode.Forbidden"/>, gdy brak uprawnienia.</summary>
    public static void Require(ClientIdentity identity, string capability)
    {
        if (!identity.Has(capability))
        {
            throw new ProtocolException(
                ErrorCode.Forbidden,
                $"Brak uprawnienia '{capability}' dla roli '{identity.Role}'.");
        }
    }
}
