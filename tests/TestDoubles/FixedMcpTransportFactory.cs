using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Mcp.Contracts;
using ModelContextProtocol.Client;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FixedMcpTransportFactory(IClientTransport transport) : IMcpTransportFactory
{
    public int CreateCalls
    {
        get; private set;
    }

    public IClientTransport Create(McpSettings settings)
    {
        CreateCalls++;
        return transport;
    }
}
