using ZseAdmin.Client.Core.Connection;
using ZseAdmin.Contracts.Protocol;

namespace ZseAdmin.Client.Core.ViewModels;

/// <summary>
/// D10/ZAD-06 — jeden stan „Brak dostępu” w UI. Odmowa połączenia (HTTP 401), <c>welcome</c>
/// bez żadnego uprawnienia albo odpowiedź <c>forbidden</c> to nie to samo co
/// <c>completeness = unavailable</c> ani „brak danych”: nie pokazujemy pustych tabel ani
/// przycisków akcji, tylko jawny komunikat. Treść nie ujawnia, czy konto lub grupa istnieje —
/// szczegóły zostają w audycie serwera.
/// </summary>
public static class AccessDenial
{
    public const string Title = "Brak dostępu do ZSE-Admin";

    public const string Message = "Brak dostępu do ZSE-Admin. Skontaktuj się z administratorem.";

    public const string AuditNote =
        "Odmowa została zapisana w audycie serwera (auth.rejected). " +
        "Konta uczniów nie mają dostępu do aplikacji.";

    /// <summary>Zastępuje tożsamość w nagłówku, żeby nie sugerować zalogowanej sesji.</summary>
    public const string Identity = "brak dostępu";

    /// <summary>Brak jakiegokolwiek uprawnienia w <c>welcome</c> = brak dostępu (deny-by-default).</summary>
    public static bool HasNoCapabilities(IReadOnlyCollection<string> capabilities) =>
        capabilities.Count == 0;

    /// <summary>
    /// Odmowa uwierzytelnienia przed upgrade'em WebSocket. <see cref="System.Net.WebSockets.ClientWebSocket"/>
    /// nie wystawia statusu HTTP, więc rozpoznajemy go po komunikacie transportu („…401…”/„Unauthorized”).
    /// </summary>
    public static bool IsAuthenticationFailure(string? transportError) =>
        transportError is { Length: > 0 } error &&
        (error.Contains("401", StringComparison.Ordinal) ||
         error.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase));

    public static bool IsForbidden(Exception exception) =>
        exception is ServerErrorException { Code: ErrorCode.Forbidden };
}
