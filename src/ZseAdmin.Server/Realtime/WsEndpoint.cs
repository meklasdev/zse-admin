using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Security;

namespace ZseAdmin.Server.Realtime;

/// <summary>
/// Punkt wejścia <c>/ws</c>: kontrola transportu (wss), upgrade, uwierzytelnienie,
/// audyt odrzuceń i oddanie sterowania sesji.
/// </summary>
public static class WsEndpoint
{
    public static async Task HandleAsync(
        HttpContext context,
        MessageRouter router,
        IRequestAuthenticator authenticator,
        ConnectionRegistry registry,
        SecurityOptions security,
        IAuditLog auditLog,
        IClock clock,
        ILogger<WsConnection> logger)
    {
        var remoteAddress = context.Connection.RemoteIpAddress?.ToString();

        // M3: na produkcji wyłącznie wss:// — niezaszyfrowany ws:// jest odrzucany już tutaj,
        // zanim powstanie jakakolwiek sesja.
        if (!SecureTransport.IsRequestAllowed(context, security))
        {
            logger.LogWarning(
                "Odrzucono połączenie ws od {Remote} — wymagany wss:// (RequireSecureTransport=true).",
                remoteAddress);

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Wymagane połączenie wss:// (HTTPS). Połączenie przez niezaszyfrowany ws:// jest odrzucone."
            });
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Wymagany upgrade do WebSocket." });
            return;
        }

        ClientIdentity? identity;
        try
        {
            identity = await authenticator.AuthenticateAsync(context, context.RequestAborted);
        }
        catch (SourceUnavailableException ex)
        {
            // AD nieosiągalne to nie 401 — klient powinien spróbować ponownie, a nie szukać innego konta.
            logger.LogError(ex, "Nie można zweryfikować tożsamości ({Source}).", ex.SourceName);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                error = $"Uwierzytelnianie jest chwilowo niedostępne ({ex.SourceName}). Spróbuj ponownie za chwilę."
            });
            return;
        }

        if (identity is null)
        {
            // Odmowa przed upgrade'em: klient dostaje 401 i nie tworzy sesji.
            logger.LogWarning("Odrzucono połączenie WS z {Remote} — brak poprawnego uwierzytelnienia.", remoteAddress);
            await TryRecordAuthDenialAsync(auditLog, clock, remoteAddress);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new WsConnection(socket, identity, router, registry, logger, remoteAddress);
        await connection.RunAsync(context.RequestAborted);
    }

    private static async Task TryRecordAuthDenialAsync(IAuditLog auditLog, IClock clock, string? remoteAddress)
    {
        try
        {
            await auditLog.RecordAsync(new AuditEntry(
                Guid.NewGuid(),
                clock.UtcNow,
                "(nieznany)",
                "(brak)",
                "auth.connect",
                "/ws",
                null,
                null,
                "denied",
                remoteAddress));
        }
        catch (Exception)
        {
            // Brak audytu nie może zmienić odpowiedzi 401 w 500 — log już istnieje w loggerze.
        }
    }
}
