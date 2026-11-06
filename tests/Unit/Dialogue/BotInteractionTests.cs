using ItmoBot.Application.Commands;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Messaging;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Dialogue;

public sealed class BotInteractionTests
{
    [Fact]
    public async Task HandleAsync_Start_SendsEmbeddedWelcomePhotoButReturningFromSettingsDoesNotRepeatIt()
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        Result<bool> actualResult = await scenario.SendAsync("/start", 123);

        // Assert
        Assert.IsType<Result<bool>.Success>(actualResult);
        var photo = Assert.Single(scenario.Telegram.SentPhotos);
        Assert.Equal(AgentChecks.ChatId(123), photo.ChatId);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, photo.Photo.Take(8));
        Assert.Empty(scenario.Telegram.Sent);
        string caption = Assert.IsType<string>(Assert.Single(scenario.Telegram.PhotoCaptions));
        Assert.Contains("/quiz", caption);
        Assert.Contains("Текущий режим: Учёба (/study)", caption);
        Assert.InRange(caption.Length, 1, 1024);
        Assert.NotNull(Assert.Single(scenario.Telegram.SentKeyboards));

        // Act
        await scenario.SendAsync("⚙️ Настройки", 123);
        await scenario.SendAsync("⬅️ Главное меню", 123);

        // Assert
        Assert.Single(scenario.Telegram.SentPhotos);
        Assert.Empty(scenario.Llm.Requests);
    }

    [Fact]
    public async Task HandleAsync_PhotoFailureStill_DeliversWelcomeAndKeyboard()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Telegram.PhotoError = new AppError(ErrorCode.TelegramUnavailable, "network-secret");

        // Act
        Result<bool> actualResult = await scenario.SendAsync("/start");

        // Assert
        Assert.IsType<Result<bool>.Success>(actualResult);
        Assert.Single(scenario.Telegram.Sent);
        Assert.NotNull(Assert.Single(scenario.Telegram.SentKeyboards));
        Assert.Empty(scenario.Llm.Requests);
    }

    [Theory]
    [InlineData("⚙️ Настройки")]
    [InlineData("/settings")]
    public async Task HandleAsync_SettingsAndBackButtons_ShowsCreativityOnlyOnSettingsPage(string settingsCommand)
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        await scenario.SendAsync("/start");

        // Assert
        var main = Assert.IsType<TelegramReplyKeyboard>(scenario.Telegram.SentKeyboards[^1]);
        Assert.Equal(9, main.Rows.SelectMany(row => row).Count());
        Assert.Contains(main.Rows.SelectMany(row => row), label => label == "🕒 Часовой пояс");
        Assert.DoesNotContain(main.Rows.SelectMany(row => row), label => label.StartsWith("Креативность", StringComparison.Ordinal));

        // Act
        await scenario.SendAsync(settingsCommand);

        // Assert
        var settings = Assert.IsType<TelegramReplyKeyboard>(scenario.Telegram.SentKeyboards[^1]);
        Assert.Equal(4, settings.Rows.SelectMany(row => row).Count(label => label.StartsWith("Креативность", StringComparison.Ordinal)));
        Assert.Contains(settings.Rows.SelectMany(row => row), label => label == "⬅️ Главное меню");

        // Act
        await scenario.SendAsync("Креативность 0.7");

        // Assert
        Assert.Same(settings, scenario.Telegram.SentKeyboards[^1]);

        // Act
        await scenario.SendAsync("⬅️ Главное меню");

        // Assert
        Assert.Same(main, scenario.Telegram.SentKeyboards[^1]);

        var snapshot = Assert.IsType<Result<ConversationSnapshot>.Success>(
            await scenario.Repository.LoadAsync(AgentChecks.ChatId(42), CancellationToken.None));
        Assert.Equal(0.7m, snapshot.Value.State.Temperature);
        Assert.Empty(scenario.Llm.Requests);
        Assert.Empty(snapshot.Value.History);
    }

    [Fact]
    public async Task HandleAsync_Progress_RemainsDuringGenerationAndIsDeletedWhenAnswerIsReady()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.OnComplete = (_, _) =>
        {
            Assert.Empty(scenario.Telegram.Deleted);
            Assert.Equal("Готовлю ответ…", Assert.Single(scenario.Telegram.Sent).Item2);
            return Task.FromResult<LlmCompletionResult>(new LlmCompletionResult.Success(
                new LlmResponse(RequestChecks.LlmText("Готовый ответ"), RequestChecks.ModelName("test"), null, null)));
        };

        // Act
        Result<bool> actualResult = await scenario.SendAsync("Вопрос", 123);

        // Assert
        Assert.IsType<Result<bool>.Success>(actualResult);

        Assert.Equal((AgentChecks.ChatId(123), TelegramChecks.MessageId(1)), Assert.Single(scenario.Telegram.Deleted));
        Assert.Equal("Готовый ответ", scenario.Telegram.Sent[^1].Item2);
        Assert.Null(scenario.Telegram.SentKeyboards[0]);
        Assert.NotNull(scenario.Telegram.SentKeyboards[1]);
    }

    [Fact]
    public async Task HandleAsync_FailedDeletion_DoesNotDiscardAnswerOrReportAnExtraError()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Telegram.DeleteError = new AppError(ErrorCode.TelegramRejectedRequest, "already-deleted");

        // Act
        Result<bool> actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<Result<bool>.Success>(actualResult);

        Assert.Single(scenario.Telegram.Deleted);
        Assert.Equal(2, scenario.Telegram.Sent.Count);
        Assert.Equal("Ответ модели", scenario.Telegram.Sent[^1].Item2);
        var snapshot = Assert.IsType<Result<ConversationSnapshot>.Success>(
            await scenario.Repository.LoadAsync(AgentChecks.ChatId(42), CancellationToken.None));
        Assert.Equal(2, snapshot.Value.History.Count);
    }

    [Theory]
    [InlineData("📚 Учёба", AssistantMode.Study)]
    [InlineData("🌍 Перевод", AssistantMode.Translate)]
    [InlineData("🧠 Опрос", AssistantMode.Quiz)]
    public async Task HandleAsync_ModeButtons_UseExistingCommandsAndClearHistory(string label, AssistantMode mode)
    {
        // Arrange
        var scenario = new BotScenario();
        await scenario.SendAsync("Первый вопрос");

        // Act
        Result<bool> actualResult = await scenario.SendAsync(label);

        // Assert
        Assert.IsType<Result<bool>.Success>(actualResult);

        var snapshot = Assert.IsType<Result<ConversationSnapshot>.Success>(
            await scenario.Repository.LoadAsync(AgentChecks.ChatId(42), CancellationToken.None));
        Assert.Equal(mode, snapshot.Value.State.Mode);
        Assert.Empty(snapshot.Value.History);
        Assert.Single(scenario.Llm.Requests);
    }

    [Theory]
    [InlineData("Креативность 0.0", 0.0)]
    [InlineData("Креативность 0.3", 0.3)]
    [InlineData("Креативность 0.7", 0.7)]
    [InlineData("Креативность 1.0", 1.0)]
    public async Task HandleAsync_CreativityButtons_ChangeSettingsWithoutCallingLlm(string label, double value)
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        Result<bool> actualResult = await scenario.SendAsync(label);

        // Assert
        Assert.IsType<Result<bool>.Success>(actualResult);

        var snapshot = Assert.IsType<Result<ConversationSnapshot>.Success>(
            await scenario.Repository.LoadAsync(AgentChecks.ChatId(42), CancellationToken.None));
        Assert.Equal((decimal)value, snapshot.Value.State.Temperature);
        Assert.Empty(snapshot.Value.History);
        Assert.Empty(scenario.Llm.Requests);
    }

    [Fact]
    public async Task HandleAsync_EveryVisibleButton_IsAValidCommandAndStartIncludesKeyboard()
    {
        // Arrange
        var menu = new BotMenu();

        // Act
        var parser = new BotCommandParser(menu);
        foreach (string label in menu.GetKeyboard(BotMenuPage.Main).Rows.Concat(menu.GetKeyboard(BotMenuPage.Settings).Rows).SelectMany(row => row))
        {

            // Assert
            Assert.IsType<BotCommandParseResult.Success>(parser.Parse(label));
        }

        Assert.IsType<BotCommandParseResult.NotCommand>(parser.Parse(TelegramChecks.TelegramText("Объясни учёбу")));
        var scenario = new BotScenario();
        Assert.IsType<Result<bool>.Success>(await scenario.SendAsync("/start"));
        Assert.NotNull(Assert.Single(scenario.Telegram.SentKeyboards));
        Assert.Empty(scenario.Telegram.Deleted);
        Assert.Empty(scenario.Llm.Requests);
    }
}
