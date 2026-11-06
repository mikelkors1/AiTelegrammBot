using System.IO.Pipelines;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ItmoBot.Tests.Support;

internal sealed class InMemoryMcpServer : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly McpServer server;
    private readonly Task running;

    public InMemoryMcpServer(McpServerOptions options)
    {
        var requests = new Pipe();
        var responses = new Pipe();
        var transport = new StreamServerTransport(requests.Reader.AsStream(), responses.Writer.AsStream(),
            "TestMcpServer", NullLoggerFactory.Instance);
        ClientTransport = new StreamClientTransport(requests.Writer.AsStream(), responses.Reader.AsStream(), NullLoggerFactory.Instance);
        server = McpServer.Create(transport, options, NullLoggerFactory.Instance);
        running = server.RunAsync(lifetime.Token);
    }

    public IClientTransport ClientTransport
    {
        get;
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        try
        {
            await server.DisposeAsync();
            await running;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // The fixture owns server cancellation.
        }
        finally
        {
            lifetime.Dispose();
        }
    }
}
