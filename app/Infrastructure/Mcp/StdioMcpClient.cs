using ItmoBot.Agent.Security;
using ItmoBot.Application.Mcp;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Mcp.Contracts;
using ItmoBot.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ItmoBot.Infrastructure.Mcp;

public sealed class StdioMcpClient(McpSettings settings, IMcpTransportFactory transports,
    McpErrorMapper errors, TimeProvider clock, ILogger logger, McpCatalogReader catalogReader) : IMcpClient
{
    public async Task<Result<IMcpSession>> ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(settings.Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var key = new McpSigningKey();
            var transport = transports.Create(settings, key);
            var client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "ItmoBot", Version = "0.1.0" },
                InitializationTimeout = settings.Timeout,
                TimeProvider = clock,
            }, NullLoggerFactory.Instance, linked.Token);
            return new Result<IMcpSession>.Success(new StdioMcpSession(client, settings.Timeout, errors, clock, logger, catalogReader,
                new ActionContextSigner(key, new ActionContextCodec(), clock)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = errors.Map(exception);
            logger.LogWarning("Не удалось подключиться к MCP-серверу: {Code}.", error.Code);
            return new Result<IMcpSession>.Failure(error);
        }
    }
}
