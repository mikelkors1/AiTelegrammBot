using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Conversations.Context;
using ItmoBot.Application.Conversations.Prompts;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Mcp;
using ItmoBot.Application.Messaging;
using ItmoBot.Application.Reminders;
using ItmoBot.Infrastructure.Mcp;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Integration.Agent;

public sealed class AgentLoopIntegrationTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_FirstCompleteReminderRequest_ShowsButtonsWithoutCreatingReminder()
    {
        // Arrange
        await using var telegram = new AgentTelegramFixture();
        await telegram.InitializeAsync();
        var storage = telegram.Storage;
        var fixture = new AgentLoopFixture();
        var tomorrow = new ItmoBot.Application.Agent.Models.UserTimeZone("Europe/Moscow")
            .ConvertInstant(storage.Clock.GetUtcNow()).Date.AddDays(1).AddHours(18).AddMinutes(30);
        var moment = new DateTimeOffset(tomorrow, TimeSpan.FromHours(3));
        var arguments = System.Text.Json.JsonSerializer.Serialize(new
        {
            text = "проверить лабораторную",
            remind_at = moment.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        });
        fixture.Replies(new LlmCompletionResult.ToolCalls([fixture.Call(name: "add_reminder", arguments: arguments)]));
        var budget = new ContextBudget(RequestChecks.TokenLimit(32768), RequestChecks.TokenLimit(2048), RequestChecks.HistoryMessageLimit(24));
        var estimator = new Utf8ContextTokenEstimator();
        var agent = new AgentCompletionService(fixture.Llm, telegram.Runtime, storage.Users,
            new AgentRequestBuilder(storage.Clock, estimator, budget, new AgentReplyParser(new ToolSchemaValidator())),
            new ToolSchemaValidator(), new ReminderProposalHandler(storage.Actions, RequestChecks.ModelName("test/model")),
            new AgentAuditWriter(storage.Audit, storage.Clock, NullLogger.Instance), storage.Clock,
            new AgentReplyParser(new ToolSchemaValidator()), RequestChecks.LlmRetryWindow(60));
        var dialogue = new DialogueService(storage.Storage.CreateRepository(),
            new ContextBuilder(new PromptCatalog([new StudyPrompt()]), estimator, budget), fixture.Llm,
            new ProcessingNotifier(telegram.Telegram), NullLogger.Instance, agent);
        var handler = telegram.CreateHandler(dialogue);

        // Act
        Result<bool> result = await handler.HandleAsync(telegram.Message("Напомни завтра в 18:30 проверить лабораторную."), CancellationToken.None);

        // Assert
        Assert.IsType<Result<bool>.Success>(result);
        Assert.Single(fixture.Llm.Requests);
        var keyboard = Assert.Single(telegram.Telegram.InlineKeyboards);
        Assert.Equal(2, keyboard.Rows[0].Count);
        Assert.Contains("Создать напоминание?", telegram.Telegram.Sent[^1].Item2);
        Assert.Contains("До подтверждения", telegram.Telegram.Sent[^1].Item2);
        Assert.Equal(0L, await telegram.ReminderCountAsync());
        await using var pending = storage.Storage.Source.CreateCommand("SELECT count(*) FROM prepared_actions WHERE status = 'pending'");
        Assert.Equal(1L, await pending.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task CompleteAsync_DialogueCallsRealMcpAnd_PersistsOnlyFinalAnswerAndOwnerAudit()
    {
        // Arrange
        await using var storage = new AgentRepositoryFixture();
        await storage.InitializeAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var client = new StdioMcpClient(new McpTestSettings().Create(), new ReminderTestTransportFactory(storage.Storage.CreateScopedConnectionString()),
            new McpErrorMapper(), TimeProvider.System, NullLogger.Instance, new McpCatalogReader(new McpCatalogMapper()));
        await using var runtime = new McpRuntime(client, NullLogger.Instance);
        Assert.IsType<Result<McpCatalog>.Success>(await runtime.InitializeAsync(timeout.Token));
        var fixture = new AgentLoopFixture();
        fixture.Replies(new LlmCompletionResult.ToolCalls([fixture.Call()]), fixture.Answer("Расписание получено из repository_schedule."));
        var budget = new ContextBudget(RequestChecks.TokenLimit(32000), RequestChecks.TokenLimit(2048), RequestChecks.HistoryMessageLimit(24));
        var estimator = new Utf8ContextTokenEstimator();
        var agent = new AgentCompletionService(fixture.Llm, runtime, storage.Users, new AgentRequestBuilder(storage.Clock, estimator, budget, new AgentReplyParser(new ToolSchemaValidator())),
            new ToolSchemaValidator(), new ReminderProposalHandler(storage.Actions, RequestChecks.ModelName("test/model")),
            new AgentAuditWriter(storage.Audit, storage.Clock, NullLogger.Instance), storage.Clock, new AgentReplyParser(new ToolSchemaValidator()), RequestChecks.LlmRetryWindow(60));
        var context = new ContextBuilder(new PromptCatalog([new StudyPrompt(), new TranslatePrompt(), new QuizPrompt()]), estimator, budget);
        var dialogue = new DialogueService(storage.Storage.CreateRepository(), context, fixture.Llm,
            new ProcessingNotifier(new FakeTelegram()), NullLogger.Instance, agent);
        var owner = storage.Owner();

        // Act
        Result<DialogueReply> result = await dialogue.RespondAsync(owner.ChatId, RequestChecks.LlmText("Расписание на 12 октября 2026"), timeout.Token,
            new AgentTurnContext(owner.OwnerId, owner.ChatId, AgentChecks.UpdateId(100)));
        var history = await storage.Storage.CreateRepository().LoadAsync(owner.ChatId, timeout.Token);
        var audit = storage.Value(await storage.Audit.GetLatestEventAsync(owner.OwnerId, owner.ChatId, timeout.Token));

        // Assert
        Assert.IsType<Result<DialogueReply>.Success>(result);
        Assert.Equal(2, fixture.Llm.Requests.Count);
        var messages = Assert.IsType<Result<ConversationSnapshot>.Success>(history).Value.History;
        Assert.Equal(2, messages.Count);
        Assert.Equal("Расписание получено из repository_schedule.", messages[1].Content);
        Assert.Equal("get_schedule", audit.Tool!);
        Assert.NotNull(audit.Duration);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task CompleteAsync_ReminderProposal_PersistsPendingActionWithoutReminderAndAuditIsIsolated()
    {
        // Arrange
        await using var storage = new AgentRepositoryFixture();
        await storage.InitializeAsync();
        var fixture = new AgentLoopFixture();
        fixture.Replies(new LlmCompletionResult.ToolCalls([fixture.Call(name: "add_reminder", arguments: "{\"text\":\"Проверить расписание\",\"remind_at\":\"2030-01-02T12:00:00+03:00\"}")]));
        var agent = new AgentCompletionService(fixture.Llm, fixture.Runtime, storage.Users,
            new AgentRequestBuilder(storage.Clock, new Utf8ContextTokenEstimator(), new ContextBudget(RequestChecks.TokenLimit(32000), RequestChecks.TokenLimit(2048), RequestChecks.HistoryMessageLimit(24)), new AgentReplyParser(new ToolSchemaValidator())),
            new ToolSchemaValidator(), new ReminderProposalHandler(storage.Actions, RequestChecks.ModelName("test/model")),
            new AgentAuditWriter(storage.Audit, storage.Clock, NullLogger.Instance), storage.Clock, new AgentReplyParser(new ToolSchemaValidator()), RequestChecks.LlmRetryWindow(60));
        var owner = storage.Owner();

        // Act
        var result = await agent.CompleteAsync(fixture.Request(), new AgentTurnContext(owner.OwnerId, owner.ChatId, AgentChecks.UpdateId(100)), CancellationToken.None);

        // Assert
        var reply = Assert.IsType<AgentCompletionResult.Success>(result);
        Assert.NotNull(reply.PendingAction);
        Assert.Equal(ItmoBot.Application.Agent.Models.PreparedActionStatus.Pending, reply.PendingAction.Status);
        Assert.Empty(fixture.Runtime.Calls);
        await using var count = storage.Storage.Source.CreateCommand("SELECT count(*) FROM reminders");
        Assert.Equal(0L, await count.ExecuteScalarAsync());
        var audit = storage.Value(await storage.Audit.GetLatestEventAsync(owner.OwnerId, owner.ChatId, CancellationToken.None));
        Assert.Equal(ItmoBot.Application.Agent.Models.AuditStatus.Pending, audit.Status);
        Assert.IsType<ItmoBot.Application.Agent.ResultTypes.AgentStorageResult<ItmoBot.Application.Agent.Models.AgentAuditEvent>.NotFound>(
            await storage.Audit.GetLatestEventAsync(storage.Owner(84).OwnerId, owner.ChatId, CancellationToken.None));
    }
}
