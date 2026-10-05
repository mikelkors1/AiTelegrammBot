using ItmoBot.Application;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.Handlers;

namespace ItmoBot.Hosting.Composition.Registrations;

public sealed class ApplicationRegistration
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IConversationLock, ConversationLock>();
        services.AddScoped<IMessageHandler, AiMessageHandler>();
        services.AddScoped<IBotApplication, BotApplication>();
    }
}
