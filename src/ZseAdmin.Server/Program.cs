using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Actions;
using ZseAdmin.Server.Infrastructure.Audit;
using ZseAdmin.Server.Infrastructure.Classrooms;
using ZseAdmin.Server.Infrastructure.Ldap;
using ZseAdmin.Server.Infrastructure.Logins;
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

var isProduction = string.Equals(builder.Environment.EnvironmentName, "Production", StringComparison.OrdinalIgnoreCase)
    || string.Equals(zseAdminOptions.Environment, "Production", StringComparison.OrdinalIgnoreCase);

// M3 — bramka wdrożeniowa: na produkcji tokeny z appsettings.json są zabronione,
// dozwolony wyłącznie wss://, a LDAP musi iść po TLS. Błąd konfiguracji = serwer nie startuje.
var securityErrors = SecurityConfigurationValidator.Validate(isProduction, zseAdminOptions, securityOptions);
if (securityErrors.Count > 0)
{
    foreach (var error in securityErrors)
    {
        Console.Error.WriteLine($"ZSE-Admin: błąd konfiguracji bezpieczeństwa: {error}");
    }

    Console.Error.WriteLine("ZSE-Admin: serwer nie startuje z niebezpieczną konfiguracją produkcyjną.");
    return 1;
}

var useActiveDirectoryAuthentication = string.Equals(
    securityOptions.Authentication.Mode,
    AuthenticationModes.ActiveDirectory,
    StringComparison.OrdinalIgnoreCase);

// Negotiate (Kerberos/NTLM) na Kestrel — tożsamość żądania (HttpContext.User) jest wejściem
// do ActiveDirectoryAuthenticator. Za IIS z uwierzytelnianiem Windows nagłówek przejmuje IIS.
if (useActiveDirectoryAuthentication)
{
    builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Negotiate.NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
}

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

// Lokalne magazyny na dysku (M2/M3): katalog danych poza repozytorium.
var dataDirectory = Path.GetFullPath(zseAdminOptions.Data.Directory);
builder.Services.AddSingleton<IAuditLog>(sp => new FileAuditLog(
    Path.Combine(dataDirectory, "audit.jsonl"),
    sp.GetRequiredService<IClock>(),
    zseAdminOptions.Audit.RetentionMonths));

if (zseAdminOptions.UseMockData)
{
    builder.Services.AddSingleton<IClassroomDirectory, MockClassroomDirectory>();
    builder.Services.AddSingleton<IComputerDirectory, MockComputerDirectory>();
    builder.Services.AddSingleton<ILoginLog, MockLoginLog>();
    builder.Services.AddSingleton<IAdDirectory, MockAdDirectory>();
    builder.Services.AddSingleton<IComputerActionExecutor, MockComputerActionExecutor>();
    builder.Services.AddSingleton<IAdAccountWriter, MockAdAccountWriter>();
    builder.Services.AddSingleton<IAdGroupMembershipResolver, MockAdGroupMembershipResolver>();
}
else
{
    // Etap M1: prawdziwe AD przez LDAP. Implementacje nie znają protokołu —
    // patrz docs/architektura.md §5 i docs/adr/0005-adaptery-ad.md.
    var ldapReader = new LdapDirectoryReader(zseAdminOptions.Directory);
    builder.Services.AddSingleton<ILdapDirectoryReader>(ldapReader);
    builder.Services.AddSingleton<ILdapWriter>(ldapReader);
    builder.Services.AddSingleton(zseAdminOptions.Directory);
    builder.Services.AddSingleton(zseAdminOptions.StatusProbe);
    builder.Services.AddSingleton(zseAdminOptions.Logins);
    builder.Services.AddSingleton(zseAdminOptions.Actions);
    builder.Services.AddSingleton<IAdInventoryReader, LdapInventoryReader>();
    builder.Services.AddSingleton<IComputerStatusProbe, StatusProbe>();
    builder.Services.AddSingleton<IClassroomDirectory, AdClassroomDirectory>();
    builder.Services.AddSingleton<IComputerDirectory, AdComputerDirectory>();
    builder.Services.AddSingleton<IAdDirectory, AdDirectory>();
    builder.Services.AddSingleton<IComputerActionExecutor, WindowsComputerActionExecutor>();
    builder.Services.AddSingleton<IAdAccountWriter, LdapAdAccountWriter>();
    builder.Services.AddSingleton<IAdGroupMembershipResolver, LdapGroupMembershipResolver>();

    // M2: historia logowań ze stacji (4624/4634/4625) z retencją 30 dni.
    // UseMockDataForLogins=true zostaje jako tryb przejściowy (M1.5) — dane przykładowe.
    if (zseAdminOptions.UseMockDataForLogins)
    {
        builder.Services.AddSingleton<ILoginLog, MockLoginLog>();
    }
    else
    {
        builder.Services.AddSingleton<ILoginEventStore>(sp => new FileLoginEventStore(
            Path.Combine(dataDirectory, "logins.jsonl"),
            zseAdminOptions.Logins.RetentionDays,
            sp.GetRequiredService<IClock>()));
        builder.Services.AddSingleton<LoginCollectionState>();
        builder.Services.AddSingleton<ISecurityEventSource, WindowsSecurityEventSource>();
        builder.Services.AddSingleton<LoginEventCollector>();
        builder.Services.AddHostedService<LoginEventCollectorService>();
        builder.Services.AddSingleton<ILoginLog, LoginLog>();
    }
}

// Uwierzytelnianie (M3): produkcja = Windows/Kerberos + grupy AD; development = tokeny.
if (useActiveDirectoryAuthentication)
{
    builder.Services.AddSingleton<IRequestAuthenticator>(sp => new ActiveDirectoryAuthenticator(
        sp.GetRequiredService<IAdGroupMembershipResolver>(),
        securityOptions,
        sp.GetRequiredService<ILogger<ActiveDirectoryAuthenticator>>()));
}
else
{
    builder.Services.AddSingleton<IRequestAuthenticator>(new TokenAuthenticator(securityOptions));
}

builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<MessageRouter>();
builder.Services.AddHostedService<StatusMonitorService>();

var app = builder.Build();

// Jednorazowe czyszczenie audytu przy starcie (retencja 12 miesięcy).
{
    var auditLog = app.Services.GetRequiredService<IAuditLog>();
    try
    {
        var removed = await auditLog.PruneAsync(DateTimeOffset.UtcNow.AddMonths(-Math.Max(1, zseAdminOptions.Audit.RetentionMonths)));
        if (removed > 0)
        {
            app.Logger.LogInformation("Retencja audytu: usunięto {Count} wpisów starszych niż {Months} miesięcy.",
                removed, zseAdminOptions.Audit.RetentionMonths);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Nie udało się wykonać retencji audytu przy starcie.");
    }
}

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

if (securityOptions.RequireSecureTransport)
{
    app.Use(async (context, next) =>
    {
        if (context.Request.IsHttps)
        {
            context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
        }

        await next();
    });
}

if (useActiveDirectoryAuthentication)
{
    app.UseAuthentication();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", (
    IClock clock,
    ZseAdminOptions options,
    SecurityOptions security,
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
        authentication = security.Authentication.Mode,
        secureTransport = security.RequireSecureTransport ? "required (wss only)" : "not required",
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
        loginsSource = options.UseMockData || options.UseMockDataForLogins
            ? "mock"
            : $"file store (M2, retencja {options.Logins.RetentionDays} dni, poll {options.Logins.PollSeconds} s)"
    }));

app.Map("/ws", async (
    HttpContext context,
    MessageRouter router,
    IRequestAuthenticator authenticator,
    ConnectionRegistry registry,
    SecurityOptions security,
    IAuditLog auditLog,
    IClock clock,
    ILogger<WsConnection> logger) =>
{
    await WsEndpoint.HandleAsync(context, router, authenticator, registry, security, auditLog, clock, logger);
});

app.Run();
return 0;
