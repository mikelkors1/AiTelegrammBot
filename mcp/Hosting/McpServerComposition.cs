using ItmoBot.Agent.Security;
using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent;
using ItmoBot.Infrastructure.PostgreSql.Agent.Contracts;
using ItmoBot.Infrastructure.PostgreSql.Agent;
using ItmoBot.Mcp.Infrastructure.Reminders;
using ItmoBot.Mcp.Infrastructure.Schedule;
using ItmoBot.Mcp.Infrastructure.Weather;
using ItmoBot.Mcp.Protocol;
using ItmoBot.Mcp.Reminders;
using ItmoBot.Mcp.Schedule.Contracts;
using ItmoBot.Mcp.Schedule;
using ItmoBot.Mcp.Validation;
using ItmoBot.Mcp.Weather.Contracts;
using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Npgsql;

namespace ItmoBot.Mcp.Hosting;

public sealed class McpServerComposition
{
    public void Configure(HostApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(5);
        });
        builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "ItmoBot.Mcp",
                Version = "0.1.0",
            };
        }).WithStdioServerTransport();
        RegisterSchedule(builder);
        RegisterWeather(builder);
        RegisterReminders(builder);
        RegisterProtocol(builder);
    }

    private void RegisterSchedule(HostApplicationBuilder builder)
    {
        var path = builder.Configuration["SCHEDULE_FILE"] ?? Path.Combine(AppContext.BaseDirectory, "Data", "schedule.json");
        var timezone = builder.Configuration["SCHEDULE_TIMEZONE"] ?? "Europe/Moscow";
        var sourcePath = ScheduleChecks.ScheduleSourcePath(path);
        builder.Services.AddSingleton(new UserTimeZone(timezone));
        builder.Services.AddSingleton<IScheduleSource>(_ => new JsonScheduleSource(sourcePath));
        builder.Services.AddSingleton<ScheduleService>();
        builder.Services.AddSingleton<ScheduleMcpEndpoint>();
    }

    private void RegisterWeather(HostApplicationBuilder builder)
    {
        if (!int.TryParse(builder.Configuration["WEATHER_HTTP_TIMEOUT_SECONDS"] ?? "5", out var timeout)
            || !int.TryParse(builder.Configuration["WEATHER_HTTP_MAX_ATTEMPTS"] ?? "2", out var attempts))
        {
            throw new ArgumentException("WEATHER_HTTP_TIMEOUT_SECONDS и WEATHER_HTTP_MAX_ATTEMPTS должны быть целыми числами.");
        }

        builder.Services.AddSingleton(new WeatherHttpSettings(WeatherChecks.WeatherHttpTimeout(timeout), WeatherChecks.WeatherHttpAttempts(attempts)));
        if (!int.TryParse(builder.Configuration["MCP_TIMEOUT_SECONDS"] ?? "15", out var deadline))
        {
            throw new ArgumentException("MCP_TIMEOUT_SECONDS должен быть целым числом.");
        }

        var requestTimeout = WeatherChecks.WeatherRequestTimeout(deadline);
        builder.Services.AddSingleton<LocationTextNormalizer>();
        builder.Services.AddSingleton<LocationQueryParser>();
        builder.Services.AddSingleton<LocationSelector>();
        builder.Services.AddSingleton<OpenMeteoJsonReader>();
        builder.Services.AddSingleton<OpenMeteoLocationParser>();
        builder.Services.AddSingleton<OpenMeteoWeatherParser>();
        builder.Services.AddSingleton<IWeatherService>(provider => new WeatherService(
            provider.GetRequiredService<IOpenMeteoClient>(), provider.GetRequiredService<LocationQueryParser>(),
            provider.GetRequiredService<OpenMeteoLocationParser>(), provider.GetRequiredService<LocationSelector>(),
            provider.GetRequiredService<OpenMeteoWeatherParser>(), requestTimeout, provider.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<WeatherMcpEndpoint>();
        builder.Services.AddSingleton<OpenMeteoRequestFactory>();
        builder.Services.AddSingleton<OpenMeteoHttpClientFactory>();
        builder.Services.AddSingleton<IOpenMeteoClient>(provider => new OpenMeteoClient(
            provider.GetRequiredService<OpenMeteoHttpClientFactory>().Create(),
            provider.GetRequiredService<OpenMeteoRequestFactory>(), provider.GetRequiredService<WeatherHttpSettings>(),
            provider.GetRequiredService<TimeProvider>()));
    }

    private void RegisterReminders(HostApplicationBuilder builder)
    {
        var keyText = builder.Configuration["MCP_SIGNING_KEY"];
        var key = string.IsNullOrEmpty(keyText) ? null : new McpSigningKey(keyText);
        builder.Services.AddSingleton<ActionContextCodec>();
        builder.Services.AddSingleton<ActionContextMetadata>();
        builder.Services.AddSingleton<ActionContextVerifier>(provider => new ActionContextVerifier(key,
            provider.GetRequiredService<ActionContextCodec>(), provider.GetRequiredService<TimeProvider>()));
        RegisterStorage(builder);
        builder.Services.AddSingleton<ReminderPayloadHasher>();
        builder.Services.AddSingleton<PreparedActionPolicy>();
        builder.Services.AddSingleton<AgentUserStore>();
        builder.Services.AddSingleton<PreparedActionStore>();
        builder.Services.AddSingleton<IPreparedActionRepository, PostgresPreparedActionRepository>();
        builder.Services.AddSingleton<IReminderRepository, PostgresReminderRepository>();
        builder.Services.AddSingleton<ReminderToolService>();
        builder.Services.AddSingleton<ReminderMcpEndpoint>();
    }

    private void RegisterStorage(HostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration["MCP_POSTGRES_CONNECTION_STRING"];
        if (string.IsNullOrEmpty(connectionString))
        {
            builder.Services.AddSingleton<IAgentTransactionExecutor, UnavailableAgentTransactionExecutor>();
            return;
        }

        var settings = new NpgsqlConnectionStringBuilder(connectionString)
        {
            IncludeErrorDetail = false,
            Timeout = 5,
            CommandTimeout = 5,
            MaxPoolSize = 5,
        };
        builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(settings.ConnectionString));
        builder.Services.AddSingleton<IAgentTransactionExecutor>(provider => new AgentTransactionExecutor(
            provider.GetRequiredService<NpgsqlDataSource>(), provider.GetRequiredService<ILogger<AgentTransactionExecutor>>()));
    }

    private void RegisterProtocol(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<McpRequestDispatcher>();
        builder.Services.AddOptions<McpServerOptions>().Configure<McpRequestDispatcher>((options, dispatcher) =>
        {
            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Tools = new ToolsCapability();
            options.Capabilities.Resources = new ResourcesCapability();
            options.Handlers.ListToolsHandler = (_, token) => dispatcher.ListToolsAsync(token);
            options.Handlers.CallToolHandler = (context, token) => dispatcher.CallAsync(context.Params, token);
            options.Handlers.ListResourcesHandler = (_, token) => dispatcher.ListResourcesAsync(token);
            options.Handlers.ReadResourceHandler = (context, token) => dispatcher.ReadAsync(context.Params, token);
        });
    }
}
