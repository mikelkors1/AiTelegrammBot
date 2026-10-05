using ItmoBot.Application.Commands;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.Dialogue;
using ItmoBot.Application.Messaging;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Telegram;

namespace ItmoBot.Hosting.Composition.Registrations;

public sealed class DialogueRegistration(LlmSettings settings)
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton(settings.Model);
        services.AddSingleton<IBotMenu, BotMenu>();
        services.AddScoped<IWelcomeImageSender, WelcomeImageSender>();
        services.AddSingleton<IBotCommandParser, BotCommandParser>();
        services.AddSingleton<ICommandReplyFormatter, CommandReplyFormatter>();
        services.AddSingleton<ITextSplitter, TelegramTextSplitter>();
        services.AddSingleton<ICodeBlockParser, CodeBlockParser>();
        services.AddSingleton<IHtmlSegmentRenderer, HtmlSegmentRenderer>();
        services.AddSingleton<ITelegramReplyFormatter, TelegramReplyFormatter>();
        services.AddSingleton<IUserErrorPresenter, UserErrorPresenter>();
        services.AddScoped<IBotCommandHandler, BotCommandHandler>();
        services.AddScoped<IProcessingNotifier, ProcessingNotifier>();
        services.AddScoped<IDialogueService, DialogueService>();
        services.AddScoped<ITextSender, TelegramTextSender>();
    }
}
