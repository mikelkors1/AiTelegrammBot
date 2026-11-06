using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Commands;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Mcp;
using ItmoBot.Application.Messaging;
using ItmoBot.Application.Reminders;
using ItmoBot.Infrastructure.Mcp;
using ItmoBot.Infrastructure.Telegram;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Support;

internal sealed class AgentTelegramFixture : IAsyncDisposable
{
    public AgentTelegramFixture()
    {
        Storage = new AgentRepositoryFixture();
        Telegram = new FakeTelegram();
        Dialogue = new PendingDialogueService();
        Conversations = new ConversationLock();
        Runtime = CreateRuntime();
        var menu = new BotMenu();
        var sender = new TelegramTextSender(Telegram, new TelegramReplyFormatter(), menu, NullLogger.Instance);
        Service = new ReminderConfirmationService(Storage.Actions, Runtime, Storage.Audit, Storage.Clock, NullLogger.Instance);
        Interactions = new ReminderInteractionHandler(Telegram, new ReminderCallbackCodec(), Service, sender, new UserErrorPresenter(), NullLogger.Instance);
        Handler = CreateHandler(Dialogue);
    }

    public AiMessageHandler CreateHandler(IDialogueService dialogue)
    {
        var menu = new BotMenu();
        var sender = new TelegramTextSender(Telegram, new TelegramReplyFormatter(), menu, NullLogger.Instance);
        return new AiMessageHandler(Conversations, new BotCommandParser(menu),
            new BotCommandHandler(Storage.Storage.CreateRepository(), new CommandReplyFormatter(RequestChecks.ModelName("test/model"))), dialogue,
            sender, new WelcomeImageSender(Telegram, menu), new UserErrorPresenter(), NullLogger.Instance,
            new AgentCommandHandler(Runtime, Storage.Users, Storage.Audit, new AgentCommandReplyFormatter(Storage.Clock)),
            Interactions, new ReminderPromptSender(Telegram, new ReminderCallbackCodec(), Storage.Clock));
    }

    public ConversationLock Conversations
    {
        get;
    }
    public AgentRepositoryFixture Storage
    {
        get;
    }
    public FakeTelegram Telegram
    {
        get;
    }
    public PendingDialogueService Dialogue
    {
        get;
    }
    public McpRuntime Runtime
    {
        get;
    }
    public ReminderConfirmationService Service
    {
        get;
    }
    public ReminderInteractionHandler Interactions
    {
        get;
    }
    public AiMessageHandler Handler
    {
        get;
    }

    public async Task InitializeAsync()
    {
        await Storage.InitializeAsync();
        Storage.Clock.Advance(DateTimeOffset.UtcNow - Storage.Clock.GetUtcNow());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Assert.IsType<Result<McpCatalog>.Success>(await Runtime.InitializeAsync(timeout.Token));
    }

    public McpRuntime CreateRuntime()
    {
        var client = new StdioMcpClient(new McpTestSettings().Create(), new ReminderTestTransportFactory(Storage.Storage.CreateScopedConnectionString()),
            new McpErrorMapper(), TimeProvider.System, NullLogger.Instance, new McpCatalogReader(new McpCatalogMapper()));
        return new McpRuntime(client, NullLogger.Instance);
    }

    public IncomingUpdate Message(string text, int update = 10, long owner = 42)
    {
        return new IncomingUpdate(AgentChecks.UpdateId(update), AgentChecks.ChatId(owner), TelegramChecks.TelegramText(text), AgentChecks.OwnerId(owner));
    }

    public IncomingUpdate Callback(PreparedAction action, bool confirm, int update = 101, long owner = 42)
    {
        return new IncomingUpdate(AgentChecks.UpdateId(update), AgentChecks.ChatId(owner), null, AgentChecks.OwnerId(owner),
            new TelegramCallback(TelegramChecks.CallbackQueryId("callback_" + update),
                new ReminderCallbackCodec().Encode(new ReminderButtonAction(action.Id, confirm)), TelegramChecks.MessageId(1)));
    }

    public async Task<long> ReminderCountAsync()
    {
        await using var command = Storage.Storage.Source.CreateCommand("SELECT count(*) FROM reminders");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        await Storage.DisposeAsync();
    }
}
