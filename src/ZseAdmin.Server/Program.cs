using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.HttpOverrides;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Audit;
using ZseAdmin.Server.Infrastructure.Classrooms;
using ZseAdmin.Server.Infrastructure.Ldap;
using ZseAdmin.Server.Infrastructure.Logins;
using ZseAdmin.Server.Infrastructure.Mock;
using ZseAdmin.Server.Infrastructure.Windows;
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

var isProduction = builder.Environment.IsProduction() ||
                   string.Equals(zseAdminOptions.Environment, "Production", StringComparison.OrdinalIgnoreCase);

if (isProduction)
{
    // Tablice w konfiguracji .NET łączą się po indeksach: pusta tablica w
    // appsettings.Production.json NIE usuwa tokenów z appsettings.json.
    // W trybie produkcyjnym czyścimy je jawnie, zanim cokolwiek je odczyta.
    securityOptions.Tokens.Clear();

    var configurationErrors = ProductionConfigurationGuard.Validate(zseAdminOptions, securityOptions);
    if (configurationErrors.Count > 0)
    {
        foreach (var error in configurationErrors)
        {
            Console.Error.WriteLine($"ZSE-Admin: błąd konfiguracji produkcyjnej: {error}");
        }

        Console.Error.WriteLine("ZSE-Admin: serwer nie startuje bez bezpiecznej konfiguracji (M3).");
        return 1;
    }
}

builder.Services.AddSingleton(zseAdminOptions);
builder.Services.AddSingleton(securityOptions);
builder.Services.AddSingleton(zseAdminOptions.Directory);
builder.Services.AddSingleton(zseAdminOptions.StatusProbe);
builder.Services.AddSingleton(zseAdminOptions.LoginHistory);
builder.Services.AddSingleton(zseAdminOptions.Audit);
builder.Services.AddSingleton(zseAdminOptions.Actions);

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
builder.Services.AddSingleton<IAuditLog, AuditLog>();
builder.Services.AddSingleton<ConfirmationStore>();
builder.Services.AddSingleton<ITeacherClassroomAccess, ConfiguredTeacherClassroomAccess>();
builder.Services.AddSingleton<IWindowsGroupProvider, WindowsGroupProvider>();
builder.Services.AddSingleton<ITransportSecurityGuard, TransportSecurityGuard>();
builder.Services.AddSingleton<LdapConnectionFactory>();

if (zseAdminOptions.UseMockData)
{
    builder.Services.AddSingleton<IClassroomDirectory, MockClassroomDirectory>();
    builder.Services.AddSingleton<IComputerDirectory, MockComputerDirectory>();
    builder.Services.AddSingleton<IAdDirectory, MockAdDirectory>();

    builder.Services.AddSingleton<IComputerControl, MockComputerControl>();
    builder.Services.AddSingleton<IAdWriteService, MockAdWriteService>();
}
else
{
    // Etap M1: prawdziwe AD przez LDAP. Implementacje nie znają protokołu —
    // patrz docs/architektura.md §5 i docs/adr/0005-adaptery-ad.md.
    builder.Services.AddSingleton<ILdapDirectoryReader, LdapDirectoryReader>();
    builder.Services.AddSingleton<ILdapDirectoryWriter, LdapDirectoryWriter>();
    builder.Services.AddSingleton<IAdInventoryReader, LdapInventoryReader>();
    builder.Services.AddSingleton<IComputerStatusProbe, StatusProbe>();
    builder.Services.AddSingleton<IClassroomDirectory, AdClassroomDirectory>();
    builder.Services.AddSingleton<IComputerDirectory, AdComputerDirectory>();
    builder.Services.AddSingleton<IAdDirectory, AdDirectory>();

    // M4: realne akcje na stacjach i zapis do AD.
    builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
    builder.Services.AddSingleton<IComputerControl, WindowsComputerControl>();
    builder.Services.AddSingleton<IAdWriteService, LdapAdWriteService>();
}

// M2: jedno źródło historii, jeden bufor z retencją, jeden kolektor (≤ 2 minuty).
var loginSource = zseAdminOptions.UseMockData ? "Mock" : zseAdminOptions.LoginHistory.Source;
builder.Services.AddSingleton<ILoginEventSource>(sp =>
{
    if (string.Equals(loginSource, "WefFile", StringComparison.OrdinalIgnoreCase))
    {
        return new WefFileEventSource(
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<LoginHistoryOptions>());
    }

    if (string.Equals(loginSource, "WindowsEventLog", StringComparison.OrdinalIgnoreCase))
    {
        return new RemoteSecurityEventSource(
            sp.GetRequiredService<IComputerDirectory>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<LoginHistoryOptions>());
    }

    return new MockLoginEventSource(
        sp.GetRequiredService<IClock>(),
        sp.GetRequiredService<LoginHistoryOptions>());
});

if (zseAdminOptions.LoginHistory.Enabled)
{
    builder.Services.AddSingleton<LoginHistoryBuffer>();
    builder.Services.AddSingleton<ILoginLog>(sp => sp.GetRequiredService<LoginHistoryBuffer>());
    builder.Services.AddHostedService<LoginCollectorService>();
}
else
{
    // Tryb awaryjny: jawnie „historia niedostępna”, nigdy pusta lista bez wyjaśnienia.
    builder.Services.AddSingleton<ILoginLog, UnavailableLoginLog>();
}

// M3: rola z grup AD (Windows) albo token developerski, zakres pracowni dla nauczyciela.
builder.Services.AddSingleton<ILoginAccessPolicy, LoginAccessPolicy>();
builder.Services.AddSingleton<ITokenAuthenticator>(sp => securityOptions.IsWindowsMode
    ? new WindowsAuthenticator(
        sp.GetRequiredService<SecurityOptions>(),
        sp.GetRequiredService<IWindowsGroupProvider>(),
        sp.GetRequiredService<ITransportSecurityGuard>())
    : new TokenAuthenticator(
        sp.GetRequiredService<SecurityOptions>(),
        sp.GetRequiredService<ITransportSecurityGuard>()));

if (securityOptions.IsWindowsMode && OperatingSystem.IsWindows())
{
    // Kestrel jako usługa Windows: Negotiate/Kerberos. W IIS/HTTP.sys tożsamość
    // dostarcza host, a AddNegotiate nie jest potrzebne.
    builder.Services
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
}

builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<MessageRouter>();
builder.Services.AddSingleton<AdminActionService>();
builder.Services.AddHostedService<StatusMonitorService>();

var app = builder.Build();

// Za zaufanym reverse proxy (IIS) X-Forwarded-Proto ustawia request.IsHttps,
// co pozwala egzekwować wss bez wystawiania Kestrel na bezpośredni HTTPS.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto
});

if (securityOptions.IsWindowsMode && OperatingSystem.IsWindows())
{
    app.UseAuthentication();
}

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", (
    IClock clock,
    ZseAdminOptions options,
    ClassroomMap map,
    ConnectionRegistry registry,
    IServiceProvider services) =>
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
        auth = new
        {
            mode = securityOptions.Mode,
            requireSecureTransport = securityOptions.RequireSecureTransport,
            teacherScope = securityOptions.EnforceTeacherClassroomScope,
            roleGroups = securityOptions.RoleGroups.Count
        },
        loginHistory = new
        {
            enabled = options.LoginHistory.Enabled,
            source = loginSource,
            pollSeconds = LoginCollectorService.ClampInterval(options.LoginHistory.PollSeconds).TotalSeconds,
            retentionDays = options.LoginHistory.RetentionDays,
            bufferedEvents = services.GetService<LoginHistoryBuffer>()?.Count,
            stations = services.GetService<LoginHistoryBuffer>()?.StationCount
        },
        audit = new
        {
            retentionMonths = options.Audit.RetentionMonths,
            file = options.Audit.FilePath is null ? "memory" : "jsonl"
        },
        actions = new
        {
            allowWrites = options.Actions.AllowWrites,
            confirmationSeconds = options.Actions.ConfirmationSeconds
        }
    }));

app.Map("/ws", async (
    HttpContext context,
    MessageRouter router,
    ITokenAuthenticator authenticator,
    ConnectionRegistry registry,
    SecurityOptions security,
    ITransportSecurityGuard transport,
    IAuditLog audit,
    ILogger<WsConnection> logger) =>
{
    await WsEndpoint.HandleAsync(
        context, router, authenticator, registry, security, transport, audit, logger);
});

app.Run();
return 0;
