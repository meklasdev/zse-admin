using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Security;

namespace ZseAdmin.Server.Realtime;

/// <summary>
/// Punkt wejścia <c>/ws</c>: sprawdza upgrade, <c>wss</c> (M3), uwierzytelnia
/// i oddaje sterowanie sesji. Odmowy również trafiają do audytu.
/// </summary>
public static class WsEndpoint
{
    public static async Task HandleAsync(
        HttpContext context,
        MessageRouter router,
        ITokenAuthenticator authenticator,
        ConnectionRegistry registry,
        SecurityOptions security,
        ITransportSecurityGuard transport,
        IAuditLog audit,
        ILogger<WsConnection> logger)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Wymagany upgrade do WebSocket." });
            return;
        }

        var remote = context.Connection.RemoteIpAddress?.ToString();
        var actor = context.User.Identity?.Name ?? "anonymous";

        if (!transport.IsAllowed(context.Request))
        {
            logger.LogWarning(
                "Odrzucono połączenie WS z {Remote} — wymagane wss (Security:RequireSecureTransport).",
                remote);

            await RecordAsync(
                audit, context.RequestAborted, actor, "transport.rejected", remote,
                "Połączenie bez wss.");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Wymagane połączenie wss (HTTPS)." });
            return;
        }

        var authentication = authenticator.Authenticate(context.Request);
        if (!authentication.Succeeded || authentication.Identity is null)
        {
            // Odmowa przed upgrade'em: klient dostaje 401 i nie tworzy sesji.
            // Kod przyczyny jest stabilny i nie zawiera danych osobowych poza nazwą konta (actor).
            var rejection = authentication.RejectionCode ?? RejectionCodes.NotAuthenticated;

            logger.LogWarning(
                "Odrzucono połączenie WS z {Remote} ({User}) — {Rejection} " +
                "(Mode={Mode}, RequireToken={RequireToken}).",
                remote, actor, rejection, security.Mode, security.RequireToken);

            await RecordAsync(
                audit, context.RequestAborted, actor, "auth.rejected", remote,
                $"Odmowa uwierzytelnienia ({rejection}): brak tokenu, roli ZSE-Admin-* " +
                "albo konto w grupie wykluczającej.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var identity = authentication.Identity;

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new WsConnection(socket, identity, router, registry, logger);
        await connection.RunAsync(context.RequestAborted);
    }

    private static Task RecordAsync(
        IAuditLog audit,
        CancellationToken cancellationToken,
        string actor,
        string action,
        string? remote,
        string detail) =>
        audit.RecordAsync(
            new AuditRecord(
                actor,
                "-",
                "connection",
                action,
                "/ws",
                null,
                null,
                "denied",
                remote,
                detail),
            cancellationToken);
}
