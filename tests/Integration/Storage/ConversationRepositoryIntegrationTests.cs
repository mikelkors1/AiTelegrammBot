using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Integration.Storage;

public sealed class ConversationRepositoryIntegrationTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task LoadAsync_NewUser_GetsStudyAndDefaultCreativity()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();

        // Act
        var snapshot = Value(await repository.LoadAsync(AgentChecks.ChatId(42), CancellationToken.None));

        // Assert
        Assert.Equal(AssistantMode.Study, snapshot.State.Mode);
        Assert.Equal(RequestChecks.Temperature(0.3m), snapshot.State.Temperature);
        Assert.Equal(RequestChecks.ConversationRevision(0), snapshot.State.Revision);
        Assert.Empty(snapshot.History);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task LoadAsync_Users_HaveSeparateSettingsAndChronologicalHistory()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        long first = AgentChecks.ChatId(42);
        long second = AgentChecks.ChatId(43);
        Value(await repository.SetTemperatureAsync(first, RequestChecks.Temperature(0.7m), CancellationToken.None));
        await TurnAsync(storage, first, "Первый запрос", "Первый ответ");
        await TurnAsync(storage, second, "Другой пользователь", "Другой ответ");
        await TurnAsync(storage, first, "Второй запрос", "Второй ответ");

        // Act
        var firstSnapshot = Value(await repository.LoadAsync(first, CancellationToken.None));
        var secondSnapshot = Value(await repository.LoadAsync(second, CancellationToken.None));

        // Assert
        Assert.Equal(new[] { "Первый запрос", "Первый ответ", "Второй запрос", "Второй ответ" }, firstSnapshot.History.Select(m => m.Content));
        Assert.Equal(new[] { LlmMessageRole.User, LlmMessageRole.Assistant, LlmMessageRole.User, LlmMessageRole.Assistant }, firstSnapshot.History.Select(m => m.Role));
        Assert.Equal(new[] { "Другой пользователь", "Другой ответ" }, secondSnapshot.History.Select(m => m.Content));
        Assert.Equal(RequestChecks.Temperature(0.7m), firstSnapshot.State.Temperature);
        Assert.Equal(RequestChecks.Temperature(0.3m), secondSnapshot.State.Temperature);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task LoadAsync_SettingsAndHistory_SurviveRecreatingRepositoryAndPool()
    {
        // Arrange
        await using var storage = await StorageAsync();
        long chat = AgentChecks.ChatId(42);
        var original = storage.CreateRepository();
        Value(await original.SetModeAsync(chat, AssistantMode.Translate, CancellationToken.None));
        Value(await original.SetTemperatureAsync(chat, RequestChecks.Temperature(1m), CancellationToken.None));
        await TurnAsync(storage, chat, "Текст", "Translation");
        await using var restartedSource = storage.CreateNewSource();
        var restarted = storage.CreateRepository(restartedSource);

        // Act
        var snapshot = Value(await restarted.LoadAsync(chat, CancellationToken.None));

        // Assert
        Assert.Equal(AssistantMode.Translate, snapshot.State.Mode);
        Assert.Equal(RequestChecks.Temperature(1m), snapshot.State.Temperature);
        Assert.Equal(new[] { "Текст", "Translation" }, snapshot.History.Select(m => m.Content));
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task LoadAsync_FailedAndPendingAttempts_AreStoredButNotPassedAsContext()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        long chat = AgentChecks.ChatId(42);
        var failed = Value(await repository.BeginTurnAsync(chat, RequestChecks.LlmText("Неудачный запрос"), CancellationToken.None));
        Value(await repository.FailTurnAsync(chat, failed.Id, CancellationToken.None));
        Value(await repository.BeginTurnAsync(chat, RequestChecks.LlmText("Незавершённый запрос"), CancellationToken.None));
        await TurnAsync(storage, chat, "Успешный запрос", "Успешный ответ");

        var snapshot = Value(await repository.LoadAsync(chat, CancellationToken.None));

        // Act
        await using var command = storage.Source.CreateCommand("SELECT count(*) FROM conversation_messages WHERE role = 'assistant'");

        // Assert
        Assert.Equal(new[] { "Успешный запрос", "Успешный ответ" }, snapshot.History.Select(m => m.Content));
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        await using var users = storage.Source.CreateCommand("SELECT count(*) FROM conversation_messages WHERE role = 'user'");
        Assert.Equal(3L, await users.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ResetAsync_Reset_ClearsOnlyOwnHistoryAndKeepsModeAndCreativity()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        long first = AgentChecks.ChatId(42);
        long second = AgentChecks.ChatId(43);
        Value(await repository.SetModeAsync(first, AssistantMode.Translate, CancellationToken.None));
        Value(await repository.SetTemperatureAsync(first, RequestChecks.Temperature(0.7m), CancellationToken.None));
        await TurnAsync(storage, first, "Первый", "Ответ");
        await TurnAsync(storage, second, "Второй", "Ответ второго");
        var pending = Value(await repository.BeginTurnAsync(first, RequestChecks.LlmText("В ожидании"), CancellationToken.None));

        // Act
        var reset = Value(await repository.ResetAsync(first, CancellationToken.None));

        // Assert
        Assert.Equal(AssistantMode.Translate, reset.Mode);
        Assert.Equal(RequestChecks.Temperature(0.7m), reset.Temperature);
        Assert.Empty(Value(await repository.LoadAsync(first, CancellationToken.None)).History);
        Assert.Equal(2, Value(await repository.LoadAsync(second, CancellationToken.None)).History.Count);
        await using var remaining = storage.Source.CreateCommand("SELECT count(*) FROM conversation_turns WHERE chat_id = 42");
        Assert.Equal(0L, await remaining.ExecuteScalarAsync());
        var late = Assert.IsType<Result<ConversationTurnStatus>.Failure>(await repository.CompleteTurnAsync(first, pending.Id, RequestChecks.LlmText("Поздний ответ"), CancellationToken.None));
        Assert.Equal(ErrorCode.ConversationTurnNotFound, late.Error.Code);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task SetModeAsync_SwitchingMode_ClearsHistoryAndRejectsOldAnswer()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        long chat = AgentChecks.ChatId(42);
        Value(await repository.SetTemperatureAsync(chat, RequestChecks.Temperature(0.7m), CancellationToken.None));
        await TurnAsync(storage, chat, "Старый вопрос", "Старый ответ");
        var pending = Value(await repository.BeginTurnAsync(chat, RequestChecks.LlmText("Ещё вопрос"), CancellationToken.None));

        var changed = Value(await repository.SetModeAsync(chat, AssistantMode.Translate, CancellationToken.None));

        // Act
        var late = await repository.CompleteTurnAsync(chat, pending.Id, RequestChecks.LlmText("Запоздалый ответ"), CancellationToken.None);

        // Assert
        Assert.Equal(AssistantMode.Translate, changed.Mode);
        Assert.Equal(RequestChecks.Temperature(0.7m), changed.Temperature);
        Assert.Equal(1L, changed.Revision);
        Assert.IsType<Result<ConversationTurnStatus>.Failure>(late);
        Assert.Empty(Value(await repository.LoadAsync(chat, CancellationToken.None)).History);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task CompleteTurnAsync_DuplicateCompletionAndOtherUsersTurn_AreRejected()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        long chat = AgentChecks.ChatId(42);
        var turn = Value(await repository.BeginTurnAsync(chat, RequestChecks.LlmText("Вопрос"), CancellationToken.None));

        var alien = await repository.CompleteTurnAsync(AgentChecks.ChatId(43), turn.Id, RequestChecks.LlmText("Чужой ответ"), CancellationToken.None);
        Value(await repository.CompleteTurnAsync(chat, turn.Id, RequestChecks.LlmText("Ответ"), CancellationToken.None));

        // Act
        var repeated = await repository.CompleteTurnAsync(chat, turn.Id, RequestChecks.LlmText("Дубликат"), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ConversationTurnNotFound, Assert.IsType<Result<ConversationTurnStatus>.Failure>(alien).Error.Code);
        Assert.Equal(ErrorCode.ConversationTurnAlreadyFinished, Assert.IsType<Result<ConversationTurnStatus>.Failure>(repeated).Error.Code);
        Assert.Equal(2, Value(await repository.LoadAsync(chat, CancellationToken.None)).History.Count);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task BeginTurnAsync_UserText_IsParameterizedAndDoesNotExecuteSql()
    {
        // Arrange
        await using var storage = await StorageAsync();
        const string text = "'); DROP TABLE conversation_users; --\n$текст ' с кавычками 👋";
        await TurnAsync(storage, AgentChecks.ChatId(42), text, "Ответ");

        // Act
        var snapshot = Value(await storage.CreateRepository().LoadAsync(AgentChecks.ChatId(42), CancellationToken.None));

        // Assert
        Assert.Equal(text, snapshot.History[0].Content);
        Assert.Equal(AssistantMode.Study, snapshot.State.Mode);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ResetAndComplete_ConcurrentResetAndCompletion_CannotRestoreClearedHistory()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        long chat = AgentChecks.ChatId(42);
        var turn = Value(await repository.BeginTurnAsync(chat, RequestChecks.LlmText("Вопрос"), CancellationToken.None));

        await Task.WhenAll(
            repository.CompleteTurnAsync(chat, turn.Id, RequestChecks.LlmText("Ответ"), CancellationToken.None),
            repository.ResetAsync(chat, CancellationToken.None));

        // Act
        var actualResult = Value(await repository.LoadAsync(chat, CancellationToken.None)).History;

        // Assert
        Assert.Empty(actualResult);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task LoadAsync_CallerCancellation_Propagates()
    {
        // Arrange
        await using var storage = await StorageAsync();
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();

        // Act
        Func<Task> act = () => storage.CreateRepository().LoadAsync(AgentChecks.ChatId(42), stopping.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    private async Task<PostgresTestScope> StorageAsync()
    {
        var storage = new PostgresTestScope();
        try
        {
            await storage.InitializeAsync();
            Assert.IsType<Result<int>.Success>(await storage.CreateMigrator().ApplyAsync(CancellationToken.None));
            return storage;
        }
        catch
        {
            await storage.DisposeAsync();
            throw;
        }
    }

    private T Value<T>(Result<T> result)
    {
        return Assert.IsType<Result<T>.Success>(result).Value;
    }

    private async Task TurnAsync(PostgresTestScope storage, long chatId, string user, string assistant)
    {
        var repository = storage.CreateRepository();
        var turn = Value(await repository.BeginTurnAsync(chatId, RequestChecks.LlmText(user), CancellationToken.None));
        Assert.Equal(ConversationTurnStatus.Succeeded, Value(await repository.CompleteTurnAsync(chatId, turn.Id, RequestChecks.LlmText(assistant), CancellationToken.None)));
    }
}
