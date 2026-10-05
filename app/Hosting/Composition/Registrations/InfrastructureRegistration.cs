using ItmoBot.Application.Contracts;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Health;
using ItmoBot.Infrastructure.Health.Contracts;
using ItmoBot.Infrastructure.Telegram;
using ItmoBot.Infrastructure.Telegram.Contracts;

namespace ItmoBot.Hosting.Composition.Registrations;

public sealed class InfrastructureRegistration(Settings settings)
{
    public void Register(IServiceCollection services)
    {
        new LlmRegistration(settings.Llm).Register(services);
        new PostgreSqlRegistration(settings).Register(services);
        services.AddSingleton<IReadinessChecker, ReadinessChecker>();
        services.AddSingleton<IHealthServerFactory, HealthServerFactory>();
        services.AddSingleton<ITelegramHttpClientFactory, TelegramHttpClientFactory>();
        services.AddSingleton<ITelegramErrorMapper, TelegramErrorMapper>();
        services.AddScoped<ITelegramGateway>(CreateTelegramGateway);
        services.AddScoped<IReadinessServer>(CreateReadinessServer);
    }

    private ITelegramGateway CreateTelegramGateway(IServiceProvider provider)
    {
        return new TelegramGateway(
            settings,
            provider.GetRequiredService<ITelegramHttpClientFactory>().Create(settings),
            provider.GetRequiredService<ITelegramErrorMapper>(),
            provider.GetRequiredService<ILogger>());
    }

    private IReadinessServer CreateReadinessServer(IServiceProvider provider)
    {
        return new ReadinessServer(
            provider.GetRequiredService<IDatabase>(),
            settings.HealthPort,
            provider.GetRequiredService<IHealthServerFactory>(),
            provider.GetRequiredService<ILogger>());
    }
}
