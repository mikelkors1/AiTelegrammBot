using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Integration;

public sealed class BotDialogueIntegrationTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task CommandsAndDialogueSurviveRestartWithRealPostgres()
    {
        // Arrange
        await using var storage = new PostgresTestScope();
        await storage.InitializeAsync();

        // Act
        var actualResult = await storage.CreateMigrator().ApplyAsync(CancellationToken.None);

        // Assert
        Assert.IsType<DatabaseMigrationResult.Success>(actualResult);

        // Arrange
        var original = new BotScenario(storage.CreateRepository());
        await original.SendAsync("/start");
        await original.SendAsync("/settings 0.7");
        await original.SendAsync("Первый вопрос", 42);
        await original.SendAsync("Другой пользователь", 43);

        await using var restartedPool = storage.CreateNewSource();
        var restarted = new BotScenario(storage.CreateRepository(restartedPool));

        // Act
        var continued = await restarted.SendAsync("Продолжение", 42);

        // Assert
        Assert.IsType<MessageHandlingResult.Success>(continued);
        var request = Assert.Single(restarted.Llm.Requests);
        Assert.Equal(new Temperature(0.7m), request.Temperature);
        Assert.Equal(new[] { "Первый вопрос", "Ответ модели", "Продолжение" }, request.Messages.Skip(1).Select(message => message.Content.Value));
        Assert.DoesNotContain(request.Messages, message => message.Content.Value == "Другой пользователь");

        // Act
        await restarted.SendAsync("/translate", 42);
        await restarted.SendAsync("/reset", 42);
        await restarted.SendAsync("/settings", 42);

        // Assert
        Assert.Contains("Перевод (/translate)", restarted.Telegram.Sent[^1].Item2);
        Assert.Contains("Коэффициент креативности ответа: 0.7", restarted.Telegram.Sent[^1].Item2);
        Assert.Single(restarted.Llm.Requests);
        var cleared = Assert.IsType<ConversationResult<ConversationSnapshot>.Success>(await restarted.Repository.LoadAsync(new ChatId(42), CancellationToken.None));
        var other = Assert.IsType<ConversationResult<ConversationSnapshot>.Success>(await restarted.Repository.LoadAsync(new ChatId(43), CancellationToken.None));
        Assert.Empty(cleared.Value.History);
        Assert.Equal(2, other.Value.History.Count);
    }
}
