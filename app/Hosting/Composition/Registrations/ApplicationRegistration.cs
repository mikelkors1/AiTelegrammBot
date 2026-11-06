using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Commands;
using ItmoBot.Application.Conversations.Context;
using ItmoBot.Application.Conversations.Prompts;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Mcp;
using ItmoBot.Application.Messaging;
using ItmoBot.Application.Reminders;
using ItmoBot.Application;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Telegram;

namespace ItmoBot.Hosting.Composition.Registrations;

public sealed class ApplicationRegistration(LlmSettings settings)
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<ConversationLock>();
        services.AddScoped<IMcpRuntime, McpRuntime>();
        services.AddScoped<IMessageHandler, AiMessageHandler>();
        services.AddScoped<IBotApplication, BotApplication>();
        RegisterContext(services);
        RegisterDialogue(services);
    }

    private void RegisterContext(IServiceCollection services)
    {
        services.AddSingleton(new ContextBudget(settings.ContextTokenLimit, settings.MaxOutputTokens, settings.HistoryMessageLimit));
        services.AddSingleton<IModePrompt, StudyPrompt>();
        services.AddSingleton<IModePrompt, TranslatePrompt>();
        services.AddSingleton<IModePrompt, QuizPrompt>();
        services.AddSingleton<PromptCatalog>();
        services.AddSingleton<Utf8ContextTokenEstimator>();
        services.AddSingleton<ContextBuilder>();
    }

    private void RegisterDialogue(IServiceCollection services)
    {
        services.AddSingleton<BotMenu>();
        services.AddScoped<WelcomeImageSender>();
        services.AddSingleton<BotCommandParser>();
        services.AddSingleton(_ => new CommandReplyFormatter(settings.Model));
        services.AddSingleton<TelegramReplyFormatter>();
        services.AddSingleton<UserErrorPresenter>();
        services.AddScoped<BotCommandHandler>();
        services.AddSingleton<ReminderCallbackCodec>();
        services.AddSingleton<AgentCommandReplyFormatter>();
        services.AddScoped<AgentCommandHandler>();
        services.AddScoped<ReminderConfirmationService>();
        services.AddScoped<ReminderInteractionHandler, ItmoBot.Application.Reminders.ReminderInteractionHandler>();
        services.AddScoped<ReminderPromptSender>();
        services.AddScoped<ProcessingNotifier>();
        services.AddSingleton<ToolSchemaValidator>();
        services.AddSingleton<AgentReplyParser>();
        services.AddScoped<AgentRequestBuilder>();
        services.AddScoped<IReminderProposalHandler>(provider => new ReminderProposalHandler(
            provider.GetRequiredService<ItmoBot.Application.Agent.Contracts.IPreparedActionRepository>(), settings.Model));
        services.AddScoped<IAgentAuditWriter, AgentAuditWriter>();
        services.AddScoped<AgentCompletionService>(CreateAgentCompletion);
        services.AddScoped<IDialogueService, DialogueService>();
        services.AddScoped<TelegramTextSender>();
    }

    private AgentCompletionService CreateAgentCompletion(IServiceProvider provider)
    {
        return new AgentCompletionService(
            provider.GetRequiredService<ItmoBot.Application.Llm.ILlmClient>(),
            provider.GetRequiredService<IMcpRuntime>(),
            provider.GetRequiredService<ItmoBot.Application.Agent.Contracts.IAgentUserRepository>(),
            provider.GetRequiredService<AgentRequestBuilder>(),
            provider.GetRequiredService<ToolSchemaValidator>(),
            provider.GetRequiredService<IReminderProposalHandler>(),
            provider.GetRequiredService<IAgentAuditWriter>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<AgentReplyParser>(), settings.RetryWindow);
    }
}
