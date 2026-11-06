using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Health;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Mcp;
using ItmoBot.Application.Messaging;
using ItmoBot.Application.Storage;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Health;
using ItmoBot.Infrastructure.Llm;
using ItmoBot.Infrastructure.Mcp.Contracts;
using ItmoBot.Infrastructure.Mcp;
using ItmoBot.Infrastructure.Telegram;

namespace ItmoBot.Hosting.Composition.Registrations;

public sealed class InfrastructureRegistration(Settings settings)
{
    public void Register(IServiceCollection services)
    {
        RegisterLlm(services);
        RegisterMcp(services);
        new PostgreSqlRegistration(settings).Register(services);
        services.AddSingleton<ReadinessChecker>();
        services.AddScoped<ITelegramGateway>(CreateTelegramGateway);
        services.AddScoped<IReadinessServer>(CreateReadinessServer);
    }

    private ITelegramGateway CreateTelegramGateway(IServiceProvider provider)
    {
        return new TelegramGateway(
            settings, TelegramGateway.CreateHttpClient(settings), provider.GetRequiredService<ILogger>());
    }

    private IReadinessServer CreateReadinessServer(IServiceProvider provider)
    {
        return new ReadinessServer(
            provider.GetRequiredService<IDatabase>(),
            settings.HealthPort,
            provider.GetRequiredService<ILogger>());
    }

    private void RegisterLlm(IServiceCollection services)
    {
        services.AddSingleton(settings.Llm);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ILlmClient>(CreateLlmClient);
    }

    private void RegisterMcp(IServiceCollection services)
    {
        services.AddSingleton(settings.Mcp);
        services.AddSingleton<IMcpTransportFactory, StdioMcpTransportFactory>();
        services.AddSingleton<McpErrorMapper>();
        services.AddSingleton<McpCatalogMapper>();
        services.AddSingleton<McpCatalogReader>();
        services.AddSingleton<IMcpClient, StdioMcpClient>();
    }

    private ILlmClient CreateLlmClient(IServiceProvider provider)
    {
        var singleAttempt = new OpenRouterLlmClient(
            settings.Llm, OpenRouterLlmClient.CreateHttpClient(settings.Llm), provider.GetRequiredService<ILogger>(), provider.GetRequiredService<AgentRequestBuilder>());
        return new RetryingLlmClient(singleAttempt, settings.Llm.RetryWindow,
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger>());
    }
}
