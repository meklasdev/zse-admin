using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Security;

namespace ZseAdmin.Server.Realtime;

/// <summary>Punkt wejścia <c>/ws</c>: sprawdza upgrade, uwierzytelnia i oddaje sterowanie sesji.</summary>
public static class WsEndpoint
{
    public static async Task HandleAsync(
        HttpContext context,
        MessageRouter router,
        ITokenAuthenticator authenticator,
        ConnectionRegistry registry,
        SecurityOptions security,
        ILogger<WsConnection> logger)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Wymagany upgrade do WebSocket." });
            return;
        }

        var identity = authenticator.Authenticate(context.Request);
        if (identity is null)
        {
            // Odmowa przed upgrade'em: klient dostaje 401 i nie tworzy sesji.
            logger.LogWarning(
                "Odrzucono połączenie WS z {Remote} — brak poprawnego tokenu (RequireToken={RequireToken}).",
                context.Connection.RemoteIpAddress,
                security.RequireToken);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new WsConnection(socket, identity, router, registry, logger);
        await connection.RunAsync(context.RequestAborted);
    }
}
