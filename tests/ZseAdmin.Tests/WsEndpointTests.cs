using System.Net.WebSockets;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Realtime;
using ZseAdmin.Server.Security;
using Xunit;

namespace ZseAdmin.Tests;

/// <summary>
/// ZAD-06 — odmowa dostępu musi nastąpić <b>przed</b> upgrade'em WebSocket:
/// HTTP 401, brak <c>welcome</c> (socket nie jest akceptowany) i wpis <c>auth.rejected</c>.
/// </summary>
public sealed class WsEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Uczen_z_grupy_wykluczajacej_dostaje_401_i_audyt_bez_upgrade()
    {
        var context = WebSocketContext("ZSE\\u.czen");
        var audit = TestRouterFactory.CreateAudit(new FakeClock(Now));
        var router = TestRouterFactory.Create(new FakeClock(Now), audit: audit);
        var security = new SecurityOptions
        {
            Mode = SecurityOptions.WindowsMode,
            RequireSecureTransport = true
        };

        var feature = (FakeWebSocketFeature)context.Features.Get<IHttpWebSocketFeature>()!;
        var authenticator = new FixedAuthenticator(
            AuthenticationResult.Reject(RejectionCodes.DeniedGroup));

        await WsEndpoint.HandleAsync(
            context,
            router,
            authenticator,
            new ConnectionRegistry(),
            security,
            new TransportSecurityGuard(security),
            audit,
            NullLogger<WsConnection>.Instance);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(feature.Accepted, "Połączenie nie może zostać przyjęte przed odmową.");

        var entries = await audit.QueryAsync(null, 10);
        var rejection = Assert.Single(entries.Items);
        Assert.Equal("auth.rejected", rejection.Action);
        Assert.Equal("denied", rejection.Result);
        Assert.Contains("denied_group", rejection.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Polaczenie_bez_wss_dostaje_403_i_wpis_transport_rejected()
    {
        var context = WebSocketContext("ZSE\\k.mazur");
        context.Request.Scheme = "http";

        var audit = TestRouterFactory.CreateAudit(new FakeClock(Now));
        var router = TestRouterFactory.Create(new FakeClock(Now), audit: audit);
        var security = new SecurityOptions
        {
            Mode = SecurityOptions.WindowsMode,
            RequireSecureTransport = true
        };

        var feature = (FakeWebSocketFeature)context.Features.Get<IHttpWebSocketFeature>()!;

        await WsEndpoint.HandleAsync(
            context,
            router,
            new FixedAuthenticator(AuthenticationResult.Reject(RejectionCodes.NotAuthenticated)),
            new ConnectionRegistry(),
            security,
            new TransportSecurityGuard(security),
            audit,
            NullLogger<WsConnection>.Instance);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(feature.Accepted);

        var entries = await audit.QueryAsync(null, 10);
        Assert.Equal("transport.rejected", Assert.Single(entries.Items).Action);
    }

    private static DefaultHttpContext WebSocketContext(string name)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, name) },
            authenticationType: "Negotiate");

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        context.Request.Scheme = "https";
        context.Features.Set<IHttpWebSocketFeature>(new FakeWebSocketFeature());
        return context;
    }

    private sealed class FixedAuthenticator : ITokenAuthenticator
    {
        private readonly AuthenticationResult _result;

        public FixedAuthenticator(AuthenticationResult result) => _result = result;

        public AuthenticationResult Authenticate(HttpRequest request) => _result;
    }

    private sealed class FakeWebSocketFeature : IHttpWebSocketFeature
    {
        public bool Accepted { get; private set; }

        public bool IsWebSocketRequest => true;

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
        {
            Accepted = true;
            return Task.FromResult<WebSocket>(new NeverUsedWebSocket());
        }
    }

    /// <summary>W teście odmowy socket nie powinien powstać; klasa tylko domyka interfejs.</summary>
    private sealed class NeverUsedWebSocket : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => WebSocketState.None;

        public override string? SubProtocol => null;

        public override void Abort() => throw new NotSupportedException();

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
