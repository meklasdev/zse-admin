using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ZseAdmin.Server;

namespace ZseAdmin.Client;

/// <summary>Uruchamia serwer mocków wewnątrz procesu WPF, wyłącznie na losowym porcie loopback.</summary>
internal sealed class LocalDemoHost : IAsyncDisposable
{
    private WebApplication? _application;

    public async Task<Uri> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null)
        {
            throw new InvalidOperationException("Lokalne API demo zostało już uruchomione.");
        }

        _application = ServerHost.Build(localDemoMode: true)
            ?? throw new InvalidOperationException("Nie udało się zbudować lokalnego API demo.");

        await _application.StartAsync(cancellationToken);

        var server = _application.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault(value =>
            value.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));

        if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out var baseAddress))
        {
            await _application.StopAsync(cancellationToken);
            throw new InvalidOperationException("Lokalne API nie udostępniło adresu loopback.");
        }

        var webSocketAddress = new UriBuilder(baseAddress)
        {
            Scheme = baseAddress.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = "/ws"
        };

        return webSocketAddress.Uri;
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is null)
        {
            return;
        }

        try
        {
            await _application.StopAsync();
        }
        finally
        {
            await _application.DisposeAsync();
            _application = null;
        }
    }
}
