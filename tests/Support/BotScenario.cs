using ItmoBot.Application.Commands;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.Context;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Dialogue;
using ItmoBot.Application.Handlers;
using ItmoBot.Application.Messaging;
using ItmoBot.Application.Models;
using ItmoBot.Application.Prompts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using ItmoBot.Infrastructure.Telegram;

namespace ItmoBot.Tests.Support;

internal sealed class BotScenario
{
    private int updateId;

    public BotScenario(IConversationRepository? repository = null, int historyLimit = 24,
        Func<ILlmClient, ILlmClient>? decorateClient = null)
    {
        Repository = repository ?? new FakeConversationRepository();
        var context = new ContextBuilder(new PromptCatalog([new StudyPrompt(), new TranslatePrompt(), new QuizPrompt()]),
            new Utf8ContextTokenEstimator(), new ContextBudget(new TokenLimit(8192), new TokenLimit(2048), new HistoryMessageLimit(historyLimit)));
        var client = decorateClient is null ? Llm : decorateClient(Llm);
        var dialogue = new DialogueService(Repository, context, client, new ProcessingNotifier(Telegram), NullLogger.Instance);
        var menu = new BotMenu();
        Handler = new AiMessageHandler(new ConversationLock(), new BotCommandParser(menu),
            new BotCommandHandler(Repository, new CommandReplyFormatter(new ModelName("qwen/qwen3.8-27b:free"))),
            dialogue, new TelegramTextSender(Telegram,
                new TelegramReplyFormatter(new CodeBlockParser(), new HtmlSegmentRenderer(), new TelegramTextSplitter()), menu, NullLogger.Instance),
            new WelcomeImageSender(Telegram, menu), new UserErrorPresenter(), NullLogger.Instance);
    }

    public IConversationRepository Repository
    {
        get;
    }
    public FakeTelegram Telegram { get; } = new();
    public FakeLlmClient Llm { get; } = new();
    public AiMessageHandler Handler
    {
        get;
    }

    public Task<MessageHandlingResult> SendAsync(string text, long chat = 42, CancellationToken cancellationToken = default)
    {
        return Handler.HandleAsync(new IncomingUpdate(new UpdateId(updateId++), new ChatId(chat), new TelegramText(text)), cancellationToken);
    }
}
