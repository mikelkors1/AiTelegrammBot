using ItmoBot.Application.Context;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.Prompts;
using ItmoBot.Configuration.Models;

namespace ItmoBot.Hosting.Composition.Registrations;

public sealed class ContextRegistration(LlmSettings settings)
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton(new ContextBudget(settings.ContextTokenLimit, settings.MaxOutputTokens, settings.HistoryMessageLimit));
        services.AddSingleton<IModePrompt, StudyPrompt>();
        services.AddSingleton<IModePrompt, TranslatePrompt>();
        services.AddSingleton<IModePrompt, QuizPrompt>();
        services.AddSingleton<IPromptCatalog, PromptCatalog>();
        services.AddSingleton<IContextTokenEstimator, Utf8ContextTokenEstimator>();
        services.AddSingleton<IContextBuilder, ContextBuilder>();
    }
}
