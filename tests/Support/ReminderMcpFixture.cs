using System.Globalization;
using System.Text.Json;
using ItmoBot.Agent.Security;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Mcp.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ItmoBot.Tests.Support;

internal sealed class ReminderMcpFixture : IAsyncDisposable
{
    private readonly AgentRepositoryFixture storage = new();
    private readonly McpSigningKey key = new();
    private IHost? host;
    private InMemoryMcpServer? server;
    private McpClient? client;

    public AgentRepositoryFixture Storage
    {
        get
        {
            return storage;
        }
    }

    public McpClient Client
    {
        get
        {
            return client!;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await storage.InitializeAsync();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MCP_SIGNING_KEY"] = key.ExportForChildProcess(),
            ["MCP_POSTGRES_CONNECTION_STRING"] = storage.Storage.CreateScopedConnectionString(),
        });
        new McpServerComposition().Configure(builder);
        builder.Services.AddSingleton<TimeProvider>(storage.Clock);
        host = builder.Build();
        server = new InMemoryMcpServer(host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value);
        client = await McpClient.CreateAsync(server.ClientTransport, cancellationToken: cancellationToken);
    }

    public CallToolRequestParams Request(PreparedAction action)
    {
        var signer = new ActionContextSigner(key, new ActionContextCodec(), storage.Clock);
        return new CallToolRequestParams
        {
            Name = "add_reminder",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["text"] = JsonSerializer.SerializeToElement(action.Payload.Text),
                ["remind_at"] = JsonSerializer.SerializeToElement(action.Payload.Schedule.Moment.ToString("O", CultureInfo.InvariantCulture)),
            },
            Meta = new ActionContextMetadata().Encode(signer.Sign(action)),
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync();
        }

        if (server is not null)
        {
            await server.DisposeAsync();
        }

        host?.Dispose();
        await storage.DisposeAsync();
    }
}
