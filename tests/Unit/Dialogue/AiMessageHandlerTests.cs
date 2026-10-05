using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Dialogue;

public sealed class AiMessageHandlerTests
{
    [Fact]
    public async Task OrdinaryMessageUsesHistoryAndPersistsOnlyActualAnswer()
    {
        // Arrange
        var scenario = new BotScenario();
        await scenario.SendAsync("Первый вопрос");

        // Act
        await scenario.SendAsync("Второй вопрос");

        // Assert
        var request = scenario.Llm.Requests[1];
        Assert.Equal(new[] { LlmMessageRole.System, LlmMessageRole.User, LlmMessageRole.Assistant, LlmMessageRole.User }, request.Messages.Select(message => message.Role));
        Assert.Equal(new[] { "Первый вопрос", "Ответ модели", "Второй вопрос" }, request.Messages.Skip(1).Select(message => message.Content.Value));
        Assert.Equal(new Temperature(0.3m), request.Temperature);
        var history = await HistoryAsync(scenario);
        Assert.Equal(4, history.Count);
        Assert.DoesNotContain(history, message => message.Content.Value == "Готовлю ответ…");
        Assert.Equal(new[] { "Готовлю ответ…", "Ответ модели", "Готовлю ответ…", "Ответ модели" }, scenario.Telegram.Sent.Select(message => message.Item2));
    }

    [Fact]
    public async Task UsersHaveSeparateSettingsAndContexts()
    {
        // Arrange
        var scenario = new BotScenario();
        await scenario.SendAsync("/settings 0.7", 42);
        await scenario.SendAsync("Первый", 42);
        await scenario.SendAsync("Другой", 43);

        // Act
        await scenario.SendAsync("Следующий", 42);

        // Assert
        Assert.Equal(new Temperature(0.3m), scenario.Llm.Requests[1].Temperature);
        Assert.DoesNotContain(scenario.Llm.Requests[1].Messages, message => message.Content.Value == "Первый");
        Assert.Equal(new Temperature(0.7m), scenario.Llm.Requests[2].Temperature);
        Assert.DoesNotContain(scenario.Llm.Requests[2].Messages, message => message.Content.Value == "Другой");
    }

    [Theory]
    [InlineData("/study", AssistantMode.Study)]
    [InlineData("/translate", AssistantMode.Translate)]
    [InlineData("/quiz", AssistantMode.Quiz)]
    public async Task SwitchingModeClearsHistoryKeepsCreativityAndDoesNotCallLlm(string command, AssistantMode expected)
    {
        // Arrange
        var scenario = new BotScenario();
        await scenario.SendAsync("/settings 0.7");
        await scenario.SendAsync("Старый вопрос");

        // Act
        var result = await scenario.SendAsync(command);

        // Assert
        Assert.IsType<MessageHandlingResult.Success>(result);
        Assert.Single(scenario.Llm.Requests);
        var state = (await SnapshotAsync(scenario)).State;
        Assert.Equal(expected, state.Mode);
        Assert.Equal(new Temperature(0.7m), state.Temperature);
        Assert.Empty(await HistoryAsync(scenario));

        // Act
        await scenario.SendAsync("Новый вопрос");

        // Assert
        Assert.DoesNotContain(scenario.Llm.Requests[^1].Messages, message => message.Content.Value == "Старый вопрос");
    }

    [Fact]
    public async Task ResetAffectsOnlyOwnHistoryAndPreservesSettings()
    {
        // Arrange
        var scenario = new BotScenario();
        await scenario.SendAsync("/translate");
        await scenario.SendAsync("/settings 1.0");
        await scenario.SendAsync("Первый", 42);
        await scenario.SendAsync("Другой", 43);

        // Act
        await scenario.SendAsync("/reset", 42);

        // Assert
        Assert.Equal(2, scenario.Llm.Requests.Count);
        Assert.Empty(await HistoryAsync(scenario, 42));
        Assert.Equal(2, (await HistoryAsync(scenario, 43)).Count);
        var state = (await SnapshotAsync(scenario)).State;
        Assert.Equal(AssistantMode.Translate, state.Mode);
        Assert.Equal(new Temperature(1m), state.Temperature);
        Assert.Equal("История очищена. Режим и коэффициент креативности ответа сохранены.", scenario.Telegram.Sent[^1].Item2);
    }

    [Theory]
    [InlineData("0.0", 0.0)]
    [InlineData("0.3", 0.3)]
    [InlineData("0.7", 0.7)]
    [InlineData("1.0", 1.0)]
    public async Task CreativityIsPersistedAndUsedOnNextRequest(string text, double expected)
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        await scenario.SendAsync("/settings " + text);

        // Assert
        Assert.Empty(scenario.Llm.Requests);
        Assert.Contains("Коэффициент креативности ответа: " + text, scenario.Telegram.Sent[^1].Item2);

        // Act
        await scenario.SendAsync("Вопрос");

        // Assert
        Assert.Equal(new Temperature((decimal)expected), Assert.Single(scenario.Llm.Requests).Temperature);
    }

    [Theory]
    [InlineData("/settings -1")]
    [InlineData("/settings 0.5")]
    [InlineData("/settings 0,7")]
    [InlineData("/settings NaN")]
    [InlineData("/settings 0.7 extra")]
    public async Task InvalidCreativityDoesNotChangeStateOrCallLlm(string command)
    {
        // Arrange
        var scenario = new BotScenario();
        await scenario.SendAsync("/settings 0.7");

        // Act
        var actualResult = await scenario.SendAsync(command);

        // Assert
        var failure = Assert.IsType<MessageHandlingResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.InvalidTemperature, failure.Error.Code);
        Assert.Empty(scenario.Llm.Requests);
        Assert.Equal(new Temperature(0.7m), (await SnapshotAsync(scenario)).State.Temperature);
        Assert.Equal("Выберите коэффициент креативности ответа: /settings 0.0, /settings 0.3, /settings 0.7 или /settings 1.0.", scenario.Telegram.Sent[^1].Item2);
    }

    [Fact]
    public async Task StartSettingsAndInvalidCommandsNeverEnterHistoryOrLlm()
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        await scenario.SendAsync("/start");

        // Assert
        Assert.Contains("/quiz", scenario.Telegram.PhotoCaptions[^1]!.Value);
        Assert.Contains("Учёба (/study)", scenario.Telegram.PhotoCaptions[^1]!.Value);

        // Act
        await scenario.SendAsync("/settings");

        // Assert
        Assert.Contains("Модель: qwen/qwen3.8-27b:free", scenario.Telegram.Sent[^1].Item2);

        // Act
        await scenario.SendAsync("/unknown-secret");

        // Assert
        Assert.Equal("Неизвестная команда. Доступны /start, /study, /translate, /quiz, /settings и /reset.", scenario.Telegram.Sent[^1].Item2);

        // Act
        await scenario.SendAsync("/reset unexpected");

        // Assert
        Assert.Empty(scenario.Llm.Requests);
        Assert.Empty(await HistoryAsync(scenario));
    }

    [Theory]
    [InlineData(ErrorCode.LlmTimeout, "Модель не успела ответить. Попробуйте отправить запрос позже.")]
    [InlineData(ErrorCode.LlmRateLimited, "Сервис модели временно ограничил запросы. Попробуйте позже.")]
    [InlineData(ErrorCode.EmptyLlmResponse, "Модель не вернула полный корректный ответ. Попробуйте повторить запрос.")]
    public async Task LlmErrorMarksAttemptFailedAndReturnsSafeExactMessage(ErrorCode code, string expected)
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.Error = new AppError(code, "raw-provider-secret");

        // Act
        var actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        var result = Assert.IsType<MessageHandlingResult.Failure>(actualResult);

        Assert.Equal(code, result.Error.Code);
        Assert.Equal(expected, scenario.Telegram.Sent[^1].Item2);
        Assert.DoesNotContain("raw-provider-secret", result.Message);
        Assert.Empty(await HistoryAsync(scenario));
        var turn = Assert.Single(((FakeConversationRepository)scenario.Repository).Turns).Value;
        Assert.Equal(ConversationTurnStatus.Failed, turn.Status);
        Assert.Null(turn.Assistant);
        scenario.Llm.Error = null;
        await scenario.SendAsync("Повторный вопрос");
        Assert.DoesNotContain(scenario.Llm.Requests[^1].Messages, message => message.Content.Value == "Вопрос");
    }

    [Fact]
    public async Task LongUnicodeResponseIsSavedWholeAndDeliveredInOrder()
    {
        // Arrange
        var scenario = new BotScenario();
        var text = new string('я', 4095) + "😀" + new string('x', 5000);
        scenario.Llm.Text = new LlmText(text);

        // Act
        var actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<MessageHandlingResult.Success>(actualResult);

        var parts = scenario.Telegram.Sent.Skip(1).Select(message => message.Item2).ToArray();
        Assert.Equal(text, string.Concat(parts));
        Assert.All(parts, part => Assert.InRange(part.Length, 1, 4096));
        Assert.Equal(text, (await HistoryAsync(scenario))[^1].Content.Value);
        Assert.Single(scenario.Llm.Requests);
    }

    [Fact]
    public async Task FailedPartStopsDeliveryWithoutRegenerationOrLosingStoredAnswer()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.Text = new LlmText(new string('x', 9000));
        scenario.Telegram.SendFailure = (_, _, call) => call == 3 ? new AppError(ErrorCode.TelegramUnavailable, "send-secret") : null;

        // Act
        var result = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<MessageHandlingResult.Failure>(result);
        Assert.Single(scenario.Llm.Requests);
        Assert.Equal(4, scenario.Telegram.SendCalls);
        Assert.Equal(new string('x', 4096), scenario.Telegram.Sent[1].Item2);
        Assert.Equal("Не удалось доставить ответ полностью. Автоматическая повторная генерация не выполняется.", scenario.Telegram.Sent[^1].Item2);
        Assert.Equal(9000, (await HistoryAsync(scenario))[^1].Content.Value.Length);
    }

    [Fact]
    public async Task FatalTelegramErrorIsPreservedWhenErrorNotificationAlsoFails()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Telegram.SendFailure = (_, _, call) => call switch
        {
            2 => new AppError(ErrorCode.TelegramUnauthorized, "token-secret"),
            3 => new AppError(ErrorCode.TelegramUnavailable, "network-secret"),
            _ => null,
        };

        // Act
        var actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        var failure = Assert.IsType<MessageHandlingResult.Failure>(actualResult);

        Assert.Equal(ErrorCode.TelegramUnauthorized, failure.Error.Code);
        Assert.DoesNotContain("secret", failure.Message);
        Assert.Single(scenario.Llm.Requests);
    }

    [Fact]
    public async Task CodeReplyIsFormattedForTelegramButStoredAsOriginalModelText()
    {
        // Arrange
        var scenario = new BotScenario();
        const string response = "Пример:\n```csharp\nConsole.WriteLine(\"Привет\");\n```";
        scenario.Llm.Text = new LlmText(response);

        // Act
        var actualResult = await scenario.SendAsync("Покажи код");

        // Assert
        Assert.IsType<MessageHandlingResult.Success>(actualResult);

        Assert.Equal(TelegramTextFormat.Plain, scenario.Telegram.SentFormats[0]);
        Assert.Equal(TelegramTextFormat.Html, scenario.Telegram.SentFormats[1]);
        Assert.Contains("<pre><code class=\"language-csharp\">", scenario.Telegram.Sent[1].Item2);
        Assert.Equal(response, (await HistoryAsync(scenario))[^1].Content.Value);
        Assert.Single(scenario.Llm.Requests);
    }

    [Fact]
    public async Task OversizedInputDoesNotStartTurnOrCallModel()
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        var actualResult = await scenario.SendAsync(new string('я', 4096));

        // Assert
        var result = Assert.IsType<MessageHandlingResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.ContextTooLarge, result.Error.Code);
        Assert.Empty(scenario.Llm.Requests);
        Assert.Equal(0, ((FakeConversationRepository)scenario.Repository).BeginCalls);
        Assert.Single(scenario.Telegram.Sent);
    }

    [Fact]
    public async Task HistoryCountLimitIsAppliedInRealDialogueFlow()
    {
        // Arrange
        var scenario = new BotScenario(historyLimit: 2);
        await scenario.SendAsync("Первый");
        await scenario.SendAsync("Второй");

        // Act
        await scenario.SendAsync("Третий");

        // Assert
        Assert.Equal(new[] { "Второй", "Ответ модели", "Третий" }, scenario.Llm.Requests[^1].Messages.Skip(1).Select(message => message.Content.Value));
        Assert.Equal(6, (await HistoryAsync(scenario)).Count);
    }

    [Fact]
    public async Task ContextVolumeLimitIsAppliedWithoutDeletingStoredMessages()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.Text = new LlmText(new string('x', 7000));
        await scenario.SendAsync("Первый");
        scenario.Llm.Text = new LlmText("Ответ модели");

        // Act
        await scenario.SendAsync("Второй");

        // Assert
        Assert.Equal(2, scenario.Llm.Requests[^1].Messages.Count);
        Assert.Equal("Второй", scenario.Llm.Requests[^1].Messages[^1].Content.Value);
        Assert.Equal(4, (await HistoryAsync(scenario)).Count);
        Assert.Equal(7000, (await HistoryAsync(scenario))[1].Content.Value.Length);
    }

    [Fact]
    public async Task NonTextAndBlankInputsDoNotCallModel()
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        var ignored = await scenario.Handler.HandleAsync(new IncomingUpdate(new UpdateId(0), null, null), CancellationToken.None);

        // Assert
        Assert.False(Assert.IsType<MessageHandlingResult.Success>(ignored).WasHandled);

        // Act
        await scenario.SendAsync("   \n");

        // Assert
        Assert.Equal("Отправьте сообщение с текстом.", scenario.Telegram.Sent[^1].Item2);
        Assert.Empty(scenario.Llm.Requests);
    }

    private async Task<ConversationSnapshot> SnapshotAsync(BotScenario scenario, long chat = 42)
    {
        return Assert.IsType<ConversationResult<ConversationSnapshot>.Success>(await scenario.Repository.LoadAsync(new ChatId(chat), CancellationToken.None)).Value;
    }

    private async Task<IReadOnlyList<LlmMessage>> HistoryAsync(BotScenario scenario, long chat = 42)
    {
        return (await SnapshotAsync(scenario, chat)).History;
    }
}
