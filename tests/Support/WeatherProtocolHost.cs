using ItmoBot.Mcp.Hosting;
using ItmoBot.Mcp.Infrastructure.Weather;
using ItmoBot.Mcp.Validation;
using ItmoBot.Mcp.Weather.Contracts;
using ItmoBot.Mcp.Weather.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ItmoBot.Tests.Support;

internal sealed class WeatherProtocolHost : IDisposable
{
    private readonly IHost host;

    public WeatherProtocolHost(HttpMessageHandler handler)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        new McpServerComposition().Configure(builder);
        builder.Services.AddSingleton<IOpenMeteoClient>(provider => new OpenMeteoClient(
            new HttpClient(handler), new OpenMeteoRequestFactory(),
            new WeatherHttpSettings(WeatherChecks.WeatherHttpTimeout(5), WeatherChecks.WeatherHttpAttempts(1)),
            provider.GetRequiredService<TimeProvider>()));
        host = builder.Build();
    }

    public McpServerOptions Options
    {
        get
        {
            return host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        }
    }

    public void Dispose()
    {
        host.Dispose();
    }
}
