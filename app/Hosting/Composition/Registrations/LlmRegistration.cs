using ItmoBot.Application.Contracts;
using ItmoBot.Application.Dialogue;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Llm;
using ItmoBot.Infrastructure.Llm.Contracts;

namespace ItmoBot.Hosting.Composition.Registrations;

public sealed class LlmRegistration(LlmSettings settings)
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILlmHttpClientFactory, LlmHttpClientFactory>();
        services.AddSingleton<ILlmErrorMapper, LlmErrorMapper>();
        services.AddSingleton<ILlmResponseParser, OpenRouterResponseParser>();
        services.AddSingleton<ILlmRequestWriter, OpenRouterRequestWriter>();
        services.AddScoped<ILlmClient>(CreateLlmClient);
    }

    private ILlmClient CreateLlmClient(IServiceProvider provider)
    {
        var singleAttempt = new OpenRouterLlmClient(
            settings,
            provider.GetRequiredService<ILlmHttpClientFactory>().Create(settings),
            provider.GetRequiredService<ILlmRequestWriter>(),
            provider.GetRequiredService<ILlmResponseParser>(),
            provider.GetRequiredService<ILlmErrorMapper>(),
            provider.GetRequiredService<ILogger>());
        return new RetryingLlmClient(singleAttempt, settings.RetryWindow,
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger>());
    }
}
