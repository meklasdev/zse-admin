using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Classrooms;
using ZseAdmin.Server.Infrastructure.Ldap;
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

// Mapowanie OU → pracownia jest walidowane przy starcie. Kolizja (jedno OU w dwóch
// pracowniach) to błąd konfiguracji, który zatrzymuje serwer — kryterium M1.
var classroomMap = ClassroomMap.Development();
if (!zseAdminOptions.UseMockData)
{
    var loaded = ClassroomMapLoader.Load(zseAdminOptions.Classrooms.Path);
    if (loaded is null)
    {
        Console.Error.WriteLine(
            $"ZSE-Admin: nie znaleziono pliku pracowni '{zseAdminOptions.Classrooms.Path}' " +
            "(ZseAdmin:Classrooms:Path). Serwer nie startuje, bo bez mapy OU nie da się przypisać komputerów.");
        return 1;
    }

    if (loaded.Map is null)
    {
        foreach (var error in loaded.Errors)
        {
            Console.Error.WriteLine($"ZSE-Admin: błąd konfiguracji pracowni: {error}");
        }

        Console.Error.WriteLine("ZSE-Admin: serwer nie startuje z błędną konfiguracją pracowni.");
        return 1;
    }

    classroomMap = loaded.Map;
}

builder.Services.AddSingleton(classroomMap);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<FreshnessService>();

if (zseAdminOptions.UseMockData)
{
    builder.Services.AddSingleton<IClassroomDirectory, MockClassroomDirectory>();
    builder.Services.AddSingleton<IComputerDirectory, MockComputerDirectory>();
    builder.Services.AddSingleton<ILoginLog, MockLoginLog>();
    builder.Services.AddSingleton<IAdDirectory, MockAdDirectory>();
}
else
{
    // Etap M1: prawdziwe AD przez LDAP. Implementacje nie znają protokołu —
    // patrz docs/architektura.md §5 i docs/adr/0005-adaptery-ad.md.
    builder.Services.AddSingleton(zseAdminOptions.Directory);
    builder.Services.AddSingleton(zseAdminOptions.StatusProbe);
    builder.Services.AddSingleton<ILdapDirectoryReader>(sp =>
        new LdapDirectoryReader(sp.GetRequiredService<DirectoryOptions>()));
    builder.Services.AddSingleton<IAdInventoryReader, LdapInventoryReader>();
    builder.Services.AddSingleton<IComputerStatusProbe, StatusProbe>();
    builder.Services.AddSingleton<IClassroomDirectory, AdClassroomDirectory>();
    builder.Services.AddSingleton<IComputerDirectory, AdComputerDirectory>();
    builder.Services.AddSingleton<IAdDirectory, AdDirectory>();

    // Źródło logowań to etap M2 — dopóki go nie ma, historia jest jawnie niedostępna,
    // a nie „pusta”. Serwer nie udaje danych, których nie ma.
    if (zseAdminOptions.UseMockDataForLogins)
    {
        builder.Services.AddSingleton<ILoginLog, MockLoginLog>();
    }
    else
    {
        builder.Services.AddSingleton<ILoginLog, UnavailableLoginLog>();
    }
}

builder.Services.AddSingleton<ITokenAuthenticator, TokenAuthenticator>();
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<MessageRouter>();
builder.Services.AddHostedService<StatusMonitorService>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", (
    IClock clock,
    ZseAdminOptions options,
    ClassroomMap map,
    ConnectionRegistry registry) =>
    Results.Ok(new
    {
        status = "ok",
        environment = options.Environment,
        serverTimeUtc = clock.UtcNow,
        activeSessions = registry.Count,
        mockData = options.UseMockData,
        classrooms = map.Classrooms.Count,
        ldap = options.UseMockData
            ? null
            : new
            {
                host = options.Directory.Host,
                domain = options.Directory.Domain,
                searchBase = options.Directory.SearchBase,
                useSsl = options.Directory.UseSsl
            },
        statusProbe = options.UseMockData
            ? null
            : new
            {
                enabled = options.StatusProbe.Enabled,
                budgetMilliseconds = options.StatusProbe.BudgetMilliseconds,
                maxParallelism = options.StatusProbe.MaxParallelism
            },
        loginsSource = options.UseMockData || options.UseMockDataForLogins ? "mock" : "unavailable (M2)"
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
return 0;
