using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ZseAdmin.Server;
using Xunit;

namespace ZseAdmin.Tests;

public sealed class LocalDemoServerTests
{
    [Fact]
    public async Task Local_demo_uruchamia_mock_api_na_losowym_adresie_loopback()
    {
        await using var app = ServerHost.Build(localDemoMode: true)!;
        await app.StartAsync();

        var addresses = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses;
        var address = Assert.Single(addresses);
        var baseUri = new Uri(address);

        Assert.Equal(IPAddress.Loopback, IPAddress.Parse(baseUri.Host));
        Assert.NotEqual(0, baseUri.Port);

        using var client = new HttpClient { BaseAddress = baseUri };
        var response = await client.GetAsync("/healthz");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("mockData").GetBoolean());
        Assert.Equal("Token", root.GetProperty("auth").GetProperty("mode").GetString());
        Assert.Equal("Mock", root.GetProperty("loginHistory").GetProperty("source").GetString());

        await app.StopAsync();
    }
}
