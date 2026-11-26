using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Mock;
using ZseAdmin.Server.Realtime;
using ZseAdmin.Server.Security;
using ZseAdmin.Server.Services;

var builder = WebApplication.CreateBuilder(args);

var zseAdminOptions = builder.Configuration
    .GetSection(ZseAdminOptions.SectionName)
    .Get<ZseAdminOptions>() ?? new ZseAdminOptions();

var securityOptions = builder.Configuration
    .GetSection(SecurityOptions.SectionName)
    .Get<SecurityOptions>() ?? new SecurityOptions();

builder.Services.AddSingleton(zseAdminOptions);
builder.Services.AddSingleton(securityOptions);

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<FreshnessService>();

// MVP: dane przykładowe. Adaptery AD/WMI/EventLog podmienia się przez DI — patrz docs/architektura.md.
builder.Services.AddSingleton<IClassroomDirectory, MockClassroomDirectory>();
builder.Services.AddSingleton<IComputerDirectory, MockComputerDirectory>();
builder.Services.AddSingleton<ILoginLog, MockLoginLog>();
builder.Services.AddSingleton<IAdDirectory, MockAdDirectory>();

builder.Services.AddSingleton<ITokenAuthenticator, TokenAuthenticator>();
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<MessageRouter>();
builder.Services.AddHostedService<StatusMonitorService>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", (IClock clock, ZseAdminOptions options, ConnectionRegistry registry) =>
    Results.Ok(new
    {
        status = "ok",
        environment = options.Environment,
        serverTimeUtc = clock.UtcNow,
        activeSessions = registry.Count,
        mockData = options.UseMockData
    }));

app.Map("/ws", async (
    HttpContext context,
    MessageRouter router,
    ITokenAuthenticator authenticator,
    ConnectionRegistry registry,
    SecurityOptions security,
    ILogger<WsConnection> logger) =>
{
    await WsEndpoint.HandleAsync(context, router, authenticator, registry, security, logger);
});

app.Run();
