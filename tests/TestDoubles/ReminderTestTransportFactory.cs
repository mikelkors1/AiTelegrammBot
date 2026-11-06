using ItmoBot.Agent.Security;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Mcp.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class ReminderTestTransportFactory(string connectionString) : IMcpTransportFactory
{
    public IClientTransport Create(McpSettings settings)
    {
        return Create(settings, new McpSigningKey());
    }

    public IClientTransport Create(McpSettings settings, McpSigningKey key)
    {
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["MCP_SIGNING_KEY"] = key.ExportForChildProcess();
        environment["MCP_POSTGRES_CONNECTION_STRING"] = connectionString;
        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "TestReminderServer",
            Command = "dotnet",
            Arguments = [settings.ServerPath],
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
        }, NullLoggerFactory.Instance);
    }
}
