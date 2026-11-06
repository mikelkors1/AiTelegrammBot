using ItmoBot.Application.Commands;
using ItmoBot.Application.Conversations.Context;
using ItmoBot.Application.Conversations.Prompts;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Messaging;
using ItmoBot.Infrastructure.Telegram;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Microsoft.Extensions.Logging.Abstractions;

namespace ItmoBot.Tests.Support;

internal sealed class BotScenario
{
    private int updateId;

    public BotScenario(IConversationRepository? repository = null, int historyLimit = 24,
        Func<ILlmClient, ILlmClient>? decorateClient = null)
    {
        Repository = repository ?? new FakeConversationRepository();
        var context = new ContextBuilder(new PromptCatalog([new StudyPrompt(), new TranslatePrompt(), new QuizPrompt()]),
            new Utf8ContextTokenEstimator(), new ContextBudget(RequestChecks.TokenLimit(8192), RequestChecks.TokenLimit(2048), RequestChecks.HistoryMessageLimit(historyLimit)));
        var client = decorateClient is null ? Llm : decorateClient(Llm);
        var dialogue = new DialogueService(Repository, context, client, new ProcessingNotifier(Telegram), NullLogger.Instance);
        var menu = new BotMenu();
        Handler = new AiMessageHandler(new ConversationLock(), new BotCommandParser(menu),
            new BotCommandHandler(Repository, new CommandReplyFormatter(RequestChecks.ModelName("qwen/qwen3.8-27b:free"))),
            dialogue, new TelegramTextSender(Telegram,
                new TelegramReplyFormatter(), menu, NullLogger.Instance),
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

    public Task<Result<bool>> SendAsync(string text, long chat = 42, CancellationToken cancellationToken = default)
    {
        return Handler.HandleAsync(new IncomingUpdate(AgentChecks.UpdateId(updateId++), AgentChecks.ChatId(chat), TelegramChecks.TelegramText(text)), cancellationToken);
    }
}
