using ItmoBot.Application.Commands;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Unit.Dialogue;

public sealed class BotInteractionTests
{
    [Fact]
    public async Task StartSendsEmbeddedWelcomePhotoButReturningFromSettingsDoesNotRepeatIt()
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        var actualResult = await scenario.SendAsync("/start", 123);

        // Assert
        Assert.IsType<MessageHandlingResult.Success>(actualResult);
        var photo = Assert.Single(scenario.Telegram.SentPhotos);
        Assert.Equal(new ChatId(123), photo.ChatId);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, photo.Photo.Take(8));
        Assert.Empty(scenario.Telegram.Sent);
        var caption = Assert.IsType<TelegramCaption>(Assert.Single(scenario.Telegram.PhotoCaptions));
        Assert.Contains("/quiz", caption.Value);
        Assert.Contains("Текущий режим: Учёба (/study)", caption.Value);
        Assert.InRange(caption.Value.Length, 1, 1024);
        Assert.NotNull(Assert.Single(scenario.Telegram.SentKeyboards));

        // Act
        await scenario.SendAsync("⚙️ Настройки", 123);
        await scenario.SendAsync("⬅️ Главное меню", 123);

        // Assert
        Assert.Single(scenario.Telegram.SentPhotos);
        Assert.Empty(scenario.Llm.Requests);
    }

    [Fact]
    public async Task PhotoFailureStillDeliversWelcomeAndKeyboard()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Telegram.PhotoError = new AppError(ErrorCode.TelegramUnavailable, "network-secret");

        // Act
        var actualResult = await scenario.SendAsync("/start");

        // Assert
        Assert.IsType<MessageHandlingResult.Success>(actualResult);
        Assert.Single(scenario.Telegram.Sent);
        Assert.NotNull(Assert.Single(scenario.Telegram.SentKeyboards));
        Assert.Empty(scenario.Llm.Requests);
    }

    [Theory]
    [InlineData("⚙️ Настройки")]
    [InlineData("/settings")]
    public async Task CreativityButtonsAppearOnlyInSettingsAndBackRestoresMainMenu(string settingsCommand)
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        await scenario.SendAsync("/start");

        // Assert
        var main = Assert.IsType<TelegramReplyKeyboard>(scenario.Telegram.SentKeyboards[^1]);
        Assert.Equal(6, main.Rows.SelectMany(row => row).Count());
        Assert.DoesNotContain(main.Rows.SelectMany(row => row), label => label.Value.StartsWith("Креативность", StringComparison.Ordinal));

        // Act
        await scenario.SendAsync(settingsCommand);

        // Assert
        var settings = Assert.IsType<TelegramReplyKeyboard>(scenario.Telegram.SentKeyboards[^1]);
        Assert.Equal(4, settings.Rows.SelectMany(row => row).Count(label => label.Value.StartsWith("Креативность", StringComparison.Ordinal)));
        Assert.Contains(settings.Rows.SelectMany(row => row), label => label.Value == "⬅️ Главное меню");

        // Act
        await scenario.SendAsync("Креативность 0.7");

        // Assert
        Assert.Same(settings, scenario.Telegram.SentKeyboards[^1]);

        // Act
        await scenario.SendAsync("⬅️ Главное меню");

        // Assert
        Assert.Same(main, scenario.Telegram.SentKeyboards[^1]);

        var snapshot = Assert.IsType<ConversationResult<ConversationSnapshot>.Success>(
            await scenario.Repository.LoadAsync(new ChatId(42), CancellationToken.None));
        Assert.Equal(0.7m, snapshot.Value.State.Temperature.Value);
        Assert.Empty(scenario.Llm.Requests);
        Assert.Empty(snapshot.Value.History);
    }

    [Fact]
    public async Task ProgressRemainsDuringGenerationAndIsDeletedWhenAnswerIsReady()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.OnComplete = (_, _) =>
        {
            Assert.Empty(scenario.Telegram.Deleted);
            Assert.Equal("Готовлю ответ…", Assert.Single(scenario.Telegram.Sent).Item2);
            return Task.FromResult<LlmCompletionResult>(new LlmCompletionResult.Success(
                new LlmResponse(new LlmText("Готовый ответ"), new ModelName("test"), null, null)));
        };

        // Act
        var actualResult = await scenario.SendAsync("Вопрос", 123);

        // Assert
        Assert.IsType<MessageHandlingResult.Success>(actualResult);

        Assert.Equal((new ChatId(123), new MessageId(1)), Assert.Single(scenario.Telegram.Deleted));
        Assert.Equal("Готовый ответ", scenario.Telegram.Sent[^1].Item2);
        Assert.Null(scenario.Telegram.SentKeyboards[0]);
        Assert.NotNull(scenario.Telegram.SentKeyboards[1]);
    }

    [Fact]
    public async Task FailedDeletionDoesNotDiscardAnswerOrReportAnExtraError()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Telegram.DeleteError = new AppError(ErrorCode.TelegramRejectedRequest, "already-deleted");

        // Act
        var actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<MessageHandlingResult.Success>(actualResult);

        Assert.Single(scenario.Telegram.Deleted);
        Assert.Equal(2, scenario.Telegram.Sent.Count);
        Assert.Equal("Ответ модели", scenario.Telegram.Sent[^1].Item2);
        var snapshot = Assert.IsType<ConversationResult<ConversationSnapshot>.Success>(
            await scenario.Repository.LoadAsync(new ChatId(42), CancellationToken.None));
        Assert.Equal(2, snapshot.Value.History.Count);
    }

    [Theory]
    [InlineData("📚 Учёба", AssistantMode.Study)]
    [InlineData("🌍 Перевод", AssistantMode.Translate)]
    [InlineData("🧠 Опрос", AssistantMode.Quiz)]
    public async Task ModeButtonsUseExistingCommandsAndClearHistory(string label, AssistantMode mode)
    {
        // Arrange
        var scenario = new BotScenario();
        await scenario.SendAsync("Первый вопрос");

        // Act
        var actualResult = await scenario.SendAsync(label);

        // Assert
        Assert.IsType<MessageHandlingResult.Success>(actualResult);

        var snapshot = Assert.IsType<ConversationResult<ConversationSnapshot>.Success>(
            await scenario.Repository.LoadAsync(new ChatId(42), CancellationToken.None));
        Assert.Equal(mode, snapshot.Value.State.Mode);
        Assert.Empty(snapshot.Value.History);
        Assert.Single(scenario.Llm.Requests);
    }

    [Theory]
    [InlineData("Креативность 0.0", 0.0)]
    [InlineData("Креативность 0.3", 0.3)]
    [InlineData("Креативность 0.7", 0.7)]
    [InlineData("Креативность 1.0", 1.0)]
    public async Task CreativityButtonsChangeSettingsWithoutCallingLlm(string label, double value)
    {
        // Arrange
        var scenario = new BotScenario();

        // Act
        var actualResult = await scenario.SendAsync(label);

        // Assert
        Assert.IsType<MessageHandlingResult.Success>(actualResult);

        var snapshot = Assert.IsType<ConversationResult<ConversationSnapshot>.Success>(
            await scenario.Repository.LoadAsync(new ChatId(42), CancellationToken.None));
        Assert.Equal((decimal)value, snapshot.Value.State.Temperature.Value);
        Assert.Empty(snapshot.Value.History);
        Assert.Empty(scenario.Llm.Requests);
    }

    [Fact]
    public async Task EveryVisibleButtonIsAValidCommandAndStartIncludesKeyboard()
    {
        // Arrange
        var menu = new BotMenu();

        // Act
        var parser = new BotCommandParser(menu);
        foreach (var label in menu.GetKeyboard(BotMenuPage.Main).Rows.Concat(menu.GetKeyboard(BotMenuPage.Settings).Rows).SelectMany(row => row))
        {

            // Assert
            Assert.IsType<BotCommandParseResult.Success>(parser.Parse(label));
        }

        Assert.IsType<BotCommandParseResult.NotCommand>(parser.Parse(new TelegramText("Объясни учёбу")));
        var scenario = new BotScenario();
        Assert.IsType<MessageHandlingResult.Success>(await scenario.SendAsync("/start"));
        Assert.NotNull(Assert.Single(scenario.Telegram.SentKeyboards));
        Assert.Empty(scenario.Telegram.Deleted);
        Assert.Empty(scenario.Llm.Requests);
    }
}
