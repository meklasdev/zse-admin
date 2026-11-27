using ZseAdmin.Contracts.Protocol;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Katalog uprawnień wymaganych przez poszczególne typy żądań (D10 — deny-by-default, fail-close).
/// <para>
/// Reguła: typ z <see cref="MessageTypes.ClientRequestTypes"/> bez wpisu w tym katalogu jest
/// odrzucany kodem <c>forbidden</c>, a nie „domyślnie dozwolony”. Wpis z wartością <c>null</c>
/// oznacza, że uprawnienie zależy od treści żądania (akcje M4 sprawdza
/// <c>AdminActionService</c>, <c>ping</c> nie wymaga niczego).
/// </para>
/// </summary>
public static class MessageCapabilities
{
    private static readonly Dictionary<string, string?> Required = new(StringComparer.Ordinal)
    {
        [MessageTypes.Ping] = null,
        [MessageTypes.ClassroomsList] = Capability.ClassroomsRead,
        [MessageTypes.ComputersList] = Capability.ComputersRead,
        [MessageTypes.LoginsQuery] = Capability.LoginsRead,
        [MessageTypes.UsersGet] = Capability.UsersRead,
        [MessageTypes.AuditQuery] = Capability.AuditRead,

        // Uprawnienie wynika z ActionPrepareRequest.Action (komputer vs konto AD) —
        // sprawdzane w AdminActionService przed wydaniem tokenu potwierdzenia.
        [MessageTypes.ActionPrepare] = null,
        [MessageTypes.ActionExecute] = null,

        // D7 = C: topologia dla wszystkich ról aplikacji; D4: listing plików dla wszystkich ról.
        [MessageTypes.TopologyGet] = Capability.TopologyRead,
        [MessageTypes.FilesList] = Capability.FilesRead,
        [MessageTypes.FilesStat] = Capability.FilesRead
    };

    /// <summary>Czy typ jest zdefiniowany w protokole jako żądanie klienta.</summary>
    public static bool IsProtocolRequest(string type) =>
        MessageTypes.ClientRequestTypes.Contains(type, StringComparer.Ordinal);

    /// <summary>Próbuje pobrać wymagane uprawnienie; <c>false</c> = brak mapowania (fail-close).</summary>
    public static bool TryGetCapability(string type, out string? capability) =>
        Required.TryGetValue(type, out capability);

    /// <summary>Typy protokołu bez przypisanego uprawnienia — powinna być pusta lista (test).</summary>
    public static IReadOnlyList<string> UnmappedProtocolRequests =>
        MessageTypes.ClientRequestTypes
            .Where(t => !Required.ContainsKey(t))
            .ToList();

    /// <summary>Typy znane routerowi (klient → serwer).</summary>
    public static IReadOnlyCollection<string> KnownRequestTypes => Required.Keys;
}
