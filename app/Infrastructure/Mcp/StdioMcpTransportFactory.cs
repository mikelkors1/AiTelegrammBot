using System.Globalization;
using ItmoBot.Agent.Security;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Mcp.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using Npgsql;

namespace ItmoBot.Infrastructure.Mcp;

public sealed class StdioMcpTransportFactory(Settings? applicationSettings = null) : IMcpTransportFactory
{
    public IClientTransport Create(McpSettings settings)
    {
        return Create(settings, new McpSigningKey());
    }

    public IClientTransport Create(McpSettings settings, McpSigningKey key)
    {
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["SCHEDULE_FILE"] = settings.Schedule.SourcePath;
        environment["SCHEDULE_TIMEZONE"] = settings.Schedule.TimeZone;
        environment["MCP_TIMEOUT_SECONDS"] = settings.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        environment["WEATHER_HTTP_TIMEOUT_SECONDS"] = settings.WeatherHttp.Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        environment["WEATHER_HTTP_MAX_ATTEMPTS"] = settings.WeatherHttp.Attempts.ToString(CultureInfo.InvariantCulture);
        environment["MCP_SIGNING_KEY"] = key.ExportForChildProcess();
        if (applicationSettings is not null)
        {
            environment["MCP_POSTGRES_CONNECTION_STRING"] = new NpgsqlConnectionStringBuilder
            {
                Host = applicationSettings.PostgresHost,
                Port = applicationSettings.PostgresPort,
                Database = applicationSettings.PostgresDb,
                Username = applicationSettings.PostgresUser,
                Password = applicationSettings.PostgresPassword.Value,
                IncludeErrorDetail = false,
            }.ConnectionString;
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            environment["DOTNET_ROOT"] = dotnetRoot;
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "ItmoBot.Mcp",
            Command = "dotnet",
            Arguments = [settings.ServerPath],
            WorkingDirectory = Path.GetDirectoryName(settings.ServerPath),
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        }, NullLoggerFactory.Instance);
    }
}
