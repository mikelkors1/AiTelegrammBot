using ItmoBot.Agent.Security;
using ItmoBot.Configuration.Models;
using ModelContextProtocol.Client;

namespace ItmoBot.Infrastructure.Mcp.Contracts;

public interface IMcpTransportFactory
{
    IClientTransport Create(McpSettings settings);

    IClientTransport Create(McpSettings settings, McpSigningKey key)
    {
        return Create(settings);
    }
}
