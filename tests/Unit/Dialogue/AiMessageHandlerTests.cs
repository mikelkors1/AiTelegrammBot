using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Messaging;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Dialogue;

public sealed class AiMessageHandlerTests
{
    [Fact]
    public async Task HandleAsync_OrdinaryMessage_UsesHistoryAndPersistsOnlyActualAnswer()
    {
        // Arrange
        var scenario = new BotScenario();
        await scenario.SendAsync("Первый вопрос");

        // Act
        await scenario.SendAsync("Второй вопрос");

        // Assert
        var request = scenario.Llm.Requests[1];
        Assert.Equal(new[] { LlmMessageRole.System, LlmMessageRole.User, LlmMessageRole.Assistant, LlmMessageRole.User }, request.Messages.Select(message => message.Role));
        Assert.Equal(new[] { "Первый вопрос", "Ответ модели", "Второй вопрос" }, request.Messages.Skip(1).Select(message => message.Content));
        Assert.Equal(RequestChecks.Temperature(0.3m), request.Temperature);
        var history = await HistoryAsync(scenario);
        Assert.Equal(4, history.Count);
        Assert.DoesNotContain(history, message => message.Content == "Готовлю ответ…");
        Assert.Equal(new[] { "Готовлю ответ…", "Ответ модели", "Готовлю ответ…", "Ответ модели" }, scenario.Telegram.Sent.Select(message => message.Item2));
    }

    [Fact]
    public async Task HandleAsync_Users_HaveSeparateSettingsAndContexts()
    {
        // Arrange
        var scenario = new BotScenario();
        await scenario.SendAsync("/settings 0.7", 42);
        await scenario.SendAsync("Первый", 42);
        await scenario.SendAsync("Другой", 43);

        // Act
        await scenario.SendAsync("Следующий", 42);

        // Assert
        Assert.Equal(RequestChecks.Temperature(0.3m), scenario.Llm.Requests[1].Temperature);
        Assert.DoesNotContain(scenario.Llm.Requests[1].Messages, message => message.Content == "Первый");
        Assert.Equal(RequestChecks.Temperature(0.7m), scenario.Llm.Requests[2].Temperature);
        Assert.DoesNotContain(scenario.Llm.Requests[2].Messages, message => message.Content == "Другой");
    }

    [Theory]
    [InlineData("/study", AssistantMode.Study)]
    [InlineData("/translate", AssistantMode.Translate)]
    [InlineData("/quiz", AssistantMode.Quiz)]
    public async Task HandleAsync_SwitchingMode_ClearsHistoryKeepsCreativityAndDoesNotCallLlm(string command, AssistantMode expected)
    {
        // Arrange
        var scenario = new BotScenario();
        await scenario.SendAsync("/settings 0.7");
        await scenario.SendAsync("Старый вопрос");

        // Act
        Result<bool> result = await scenario.SendAsync(command);

        // Assert
        Assert.IsType<Result<bool>.Success>(result);
        Assert.Single(scenario.Llm.Requests);
        var state = (await SnapshotAsync(scenario)).State;
        Assert.Equal(expected, state.Mode);
        Assert.Equal(RequestChecks.Temperature(0.7m), state.Temperature);
        Assert.Empty(await HistoryAsync(scenario));

        // Act
        await scenario.SendAsync("Новый вопрос");

        // Assert
        Assert.DoesNotContain(scenario.Llm.Requests[^1].Messages, message => message.Content == "Старый вопрос");
    }

    [Fact]
    public async Task HandleAsync_Reset_AffectsOnlyOwnHistoryAndPreservesSettings()
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
        Assert.Equal(RequestChecks.Temperature(1m), state.Temperature);
        Assert.Equal("История очищена. Режим и коэффициент креативности ответа сохранены.", scenario.Telegram.Sent[^1].Item2);
    }

    [Theory]
    [InlineData("0.0", 0.0)]
    [InlineData("0.3", 0.3)]
    [InlineData("0.7", 0.7)]
    [InlineData("1.0", 1.0)]
    public async Task HandleAsync_Creativity_IsPersistedAndUsedOnNextRequest(string text, double expected)
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
        Assert.Equal(RequestChecks.Temperature((decimal)expected), Assert.Single(scenario.Llm.Requests).Temperature);
    }

    [Theory]
    [InlineData("/settings -1")]
    [InlineData("/settings 0.5")]
    [InlineData("/settings 2")]
    [InlineData("/settings 0,7")]
    [InlineData("/settings NaN")]
    [InlineData("/settings 0.7 extra")]
    public async Task HandleAsync_InvalidCreativity_DoesNotChangeStateOrCallLlm(string command)
    {
        // Arrange
        var scenario = new BotScenario();
        await scenario.SendAsync("/settings 0.7");

        // Act
        Result<bool> actualResult = await scenario.SendAsync(command);

        // Assert
        Result<bool>.Failure failure = Assert.IsType<Result<bool>.Failure>(actualResult);
        Assert.Equal(ErrorCode.InvalidTemperature, failure.Error.Code);
        Assert.Empty(scenario.Llm.Requests);
        Assert.Equal(RequestChecks.Temperature(0.7m), (await SnapshotAsync(scenario)).State.Temperature);
        Assert.Equal("Выберите коэффициент креативности ответа: /settings 0.0, /settings 0.3, /settings 0.7 или /settings 1.0.", scenario.Telegram.Sent[^1].Item2);
    }

    [Fact]
    public async Task HandleAsync_StartSettingsAndInvalidCommands_NeverEnterHistoryOrLlm()
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        await scenario.SendAsync("/start");

        // Assert
        Assert.Contains("/quiz", scenario.Telegram.PhotoCaptions[^1]!);
        Assert.Contains("Учёба (/study)", scenario.Telegram.PhotoCaptions[^1]!);

        // Act
        await scenario.SendAsync("/settings");

        // Assert
        Assert.Contains("Модель: qwen/qwen3.8-27b:free", scenario.Telegram.Sent[^1].Item2);

        // Act
        await scenario.SendAsync("/unknown-secret");

        // Assert
        Assert.Equal("Неизвестная команда. Доступны /start, /study, /translate, /quiz, /settings, /reset, /tools, /timezone и /why.", scenario.Telegram.Sent[^1].Item2);

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
    public async Task HandleAsync_LlmErrorMarksAttemptFailedAnd_ReturnsSafeExactMessage(ErrorCode code, string expected)
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.Error = new AppError(code, "raw-provider-secret");

        // Act
        Result<bool> actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Result<bool>.Failure result = Assert.IsType<Result<bool>.Failure>(actualResult);

        Assert.Equal(code, result.Error.Code);
        Assert.Equal(expected, scenario.Telegram.Sent[^1].Item2);
        Assert.DoesNotContain("raw-provider-secret", result.Message);
        Assert.Empty(await HistoryAsync(scenario));
        var turn = Assert.Single(((FakeConversationRepository)scenario.Repository).Turns).Value;
        Assert.Equal(ConversationTurnStatus.Failed, turn.Status);
        Assert.Null(turn.Assistant);
        scenario.Llm.Error = null;
        await scenario.SendAsync("Повторный вопрос");
        Assert.DoesNotContain(scenario.Llm.Requests[^1].Messages, message => message.Content == "Вопрос");
    }

    [Fact]
    public async Task HandleAsync_LongUnicodeResponse_IsSavedWholeAndDeliveredInOrder()
    {
        // Arrange
        var scenario = new BotScenario();
        var text = new string('я', 4095) + "😀" + new string('x', 5000);
        scenario.Llm.Text = RequestChecks.LlmText(text);

        // Act
        Result<bool> actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<Result<bool>.Success>(actualResult);

        var parts = scenario.Telegram.Sent.Skip(1).Select(message => message.Item2).ToArray();
        Assert.Equal(text, string.Concat(parts));
        Assert.All(parts, part => Assert.InRange(part.Length, 1, 4096));
        Assert.Equal(text, (await HistoryAsync(scenario))[^1].Content);
        Assert.Single(scenario.Llm.Requests);
    }

    [Fact]
    public async Task HandleAsync_FailedPart_StopsDeliveryWithoutRegenerationOrLosingStoredAnswer()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.Text = RequestChecks.LlmText(new string('x', 9000));
        scenario.Telegram.SendFailure = (_, _, call) => call == 3 ? new AppError(ErrorCode.TelegramUnavailable, "send-secret") : null;

        // Act
        Result<bool> result = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<Result<bool>.Failure>(result);
        Assert.Single(scenario.Llm.Requests);
        Assert.Equal(4, scenario.Telegram.SendCalls);
        Assert.Equal(new string('x', 4096), scenario.Telegram.Sent[1].Item2);
        Assert.Equal("Не удалось доставить ответ полностью. Автоматическая повторная генерация не выполняется.", scenario.Telegram.Sent[^1].Item2);
        Assert.Equal(9000, (await HistoryAsync(scenario))[^1].Content.Length);
    }

    [Fact]
    public async Task HandleAsync_FatalTelegramError_IsPreservedWhenErrorNotificationAlsoFails()
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
        Result<bool> actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Result<bool>.Failure failure = Assert.IsType<Result<bool>.Failure>(actualResult);

        Assert.Equal(ErrorCode.TelegramUnauthorized, failure.Error.Code);
        Assert.DoesNotContain("secret", failure.Message);
        Assert.Single(scenario.Llm.Requests);
    }

    [Fact]
    public async Task HandleAsync_CodeReply_IsFormattedForTelegramButStoredAsOriginalModelText()
    {
        // Arrange
        var scenario = new BotScenario();
        const string response = "Пример:\n```csharp\nConsole.WriteLine(\"Привет\");\n```";
        scenario.Llm.Text = RequestChecks.LlmText(response);

        // Act
        Result<bool> actualResult = await scenario.SendAsync("Покажи код");

        // Assert
        Assert.IsType<Result<bool>.Success>(actualResult);

        Assert.Equal(TelegramTextFormat.Plain, scenario.Telegram.SentFormats[0]);
        Assert.Equal(TelegramTextFormat.Html, scenario.Telegram.SentFormats[1]);
        Assert.Contains("<pre><code class=\"language-csharp\">", scenario.Telegram.Sent[1].Item2);
        Assert.Equal(response, (await HistoryAsync(scenario))[^1].Content);
        Assert.Single(scenario.Llm.Requests);
    }

    [Fact]
    public async Task HandleAsync_OversizedInput_DoesNotStartTurnOrCallModel()
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        Result<bool> actualResult = await scenario.SendAsync(new string('я', 4096));

        // Assert
        Result<bool>.Failure result = Assert.IsType<Result<bool>.Failure>(actualResult);
        Assert.Equal(ErrorCode.ContextTooLarge, result.Error.Code);
        Assert.Empty(scenario.Llm.Requests);
        Assert.Equal(0, ((FakeConversationRepository)scenario.Repository).BeginCalls);
        Assert.Single(scenario.Telegram.Sent);
    }

    [Fact]
    public async Task HandleAsync_HistoryCountLimit_IsAppliedInRealDialogueFlow()
    {
        // Arrange
        var scenario = new BotScenario(historyLimit: 2);
        await scenario.SendAsync("Первый");
        await scenario.SendAsync("Второй");

        // Act
        await scenario.SendAsync("Третий");

        // Assert
        Assert.Equal(new[] { "Второй", "Ответ модели", "Третий" }, scenario.Llm.Requests[^1].Messages.Skip(1).Select(message => message.Content));
        Assert.Equal(6, (await HistoryAsync(scenario)).Count);
    }

    [Fact]
    public async Task HandleAsync_ContextVolumeLimit_IsAppliedWithoutDeletingStoredMessages()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.Text = RequestChecks.LlmText(new string('x', 7000));
        await scenario.SendAsync("Первый");
        scenario.Llm.Text = RequestChecks.LlmText("Ответ модели");

        // Act
        await scenario.SendAsync("Второй");

        // Assert
        Assert.Equal(2, scenario.Llm.Requests[^1].Messages.Count);
        Assert.Equal("Второй", scenario.Llm.Requests[^1].Messages[^1].Content);
        Assert.Equal(4, (await HistoryAsync(scenario)).Count);
        Assert.Equal(7000, (await HistoryAsync(scenario))[1].Content.Length);
    }

    [Fact]
    public async Task HandleAsync_NonTextAndBlankInputs_DoNotCallModel()
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        Result<bool> ignored = await scenario.Handler.HandleAsync(new IncomingUpdate(AgentChecks.UpdateId(0), null, null), CancellationToken.None);

        // Assert
        Assert.False(Assert.IsType<Result<bool>.Success>(ignored).Value);

        // Act
        await scenario.SendAsync("   \n");

        // Assert
        Assert.Equal("Отправьте сообщение с текстом.", scenario.Telegram.Sent[^1].Item2);
        Assert.Empty(scenario.Llm.Requests);
    }

    private async Task<ConversationSnapshot> SnapshotAsync(BotScenario scenario, long chat = 42)
    {
        return Assert.IsType<Result<ConversationSnapshot>.Success>(await scenario.Repository.LoadAsync(AgentChecks.ChatId(chat), CancellationToken.None)).Value;
    }

    private async Task<IReadOnlyList<LlmMessage>> HistoryAsync(BotScenario scenario, long chat = 42)
    {
        return (await SnapshotAsync(scenario, chat)).History;
    }
}
