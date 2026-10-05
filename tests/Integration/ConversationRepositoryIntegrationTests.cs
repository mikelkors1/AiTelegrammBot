using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Integration;

public sealed class ConversationRepositoryIntegrationTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task NewUserGetsStudyAndDefaultCreativity()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();

        // Act
        var snapshot = Value(await repository.LoadAsync(new ChatId(42), CancellationToken.None));

        // Assert
        Assert.Equal(AssistantMode.Study, snapshot.State.Mode);
        Assert.Equal(new Temperature(0.3m), snapshot.State.Temperature);
        Assert.Equal(ConversationRevision.Zero, snapshot.State.Revision);
        Assert.Empty(snapshot.History);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task UsersHaveSeparateSettingsAndChronologicalHistory()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        var first = new ChatId(42);
        var second = new ChatId(43);
        Value(await repository.SetTemperatureAsync(first, new Temperature(0.7m), CancellationToken.None));
        await TurnAsync(storage, first, "Первый запрос", "Первый ответ");
        await TurnAsync(storage, second, "Другой пользователь", "Другой ответ");
        await TurnAsync(storage, first, "Второй запрос", "Второй ответ");

        // Act
        var firstSnapshot = Value(await repository.LoadAsync(first, CancellationToken.None));
        var secondSnapshot = Value(await repository.LoadAsync(second, CancellationToken.None));

        // Assert
        Assert.Equal(new[] { "Первый запрос", "Первый ответ", "Второй запрос", "Второй ответ" }, firstSnapshot.History.Select(m => m.Content.Value));
        Assert.Equal(new[] { LlmMessageRole.User, LlmMessageRole.Assistant, LlmMessageRole.User, LlmMessageRole.Assistant }, firstSnapshot.History.Select(m => m.Role));
        Assert.Equal(new[] { "Другой пользователь", "Другой ответ" }, secondSnapshot.History.Select(m => m.Content.Value));
        Assert.Equal(new Temperature(0.7m), firstSnapshot.State.Temperature);
        Assert.Equal(new Temperature(0.3m), secondSnapshot.State.Temperature);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task SettingsAndHistorySurviveRecreatingRepositoryAndPool()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var chat = new ChatId(42);
        var original = storage.CreateRepository();
        Value(await original.SetModeAsync(chat, AssistantMode.Translate, CancellationToken.None));
        Value(await original.SetTemperatureAsync(chat, new Temperature(1m), CancellationToken.None));
        await TurnAsync(storage, chat, "Текст", "Translation");
        await using var restartedSource = storage.CreateNewSource();
        var restarted = storage.CreateRepository(restartedSource);

        // Act
        var snapshot = Value(await restarted.LoadAsync(chat, CancellationToken.None));

        // Assert
        Assert.Equal(AssistantMode.Translate, snapshot.State.Mode);
        Assert.Equal(new Temperature(1m), snapshot.State.Temperature);
        Assert.Equal(new[] { "Текст", "Translation" }, snapshot.History.Select(m => m.Content.Value));
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task FailedAndPendingAttemptsAreStoredButNotPassedAsContext()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        var chat = new ChatId(42);
        var failed = Value(await repository.BeginTurnAsync(chat, new LlmText("Неудачный запрос"), CancellationToken.None));
        Value(await repository.FailTurnAsync(chat, failed.Id, CancellationToken.None));
        Value(await repository.BeginTurnAsync(chat, new LlmText("Незавершённый запрос"), CancellationToken.None));
        await TurnAsync(storage, chat, "Успешный запрос", "Успешный ответ");

        var snapshot = Value(await repository.LoadAsync(chat, CancellationToken.None));

        // Act
        await using var command = storage.Source.CreateCommand("SELECT count(*) FROM conversation_messages WHERE role = 'assistant'");

        // Assert
        Assert.Equal(new[] { "Успешный запрос", "Успешный ответ" }, snapshot.History.Select(m => m.Content.Value));
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        await using var users = storage.Source.CreateCommand("SELECT count(*) FROM conversation_messages WHERE role = 'user'");
        Assert.Equal(3L, await users.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ResetClearsOnlyOwnHistoryAndKeepsModeAndCreativity()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        var first = new ChatId(42);
        var second = new ChatId(43);
        Value(await repository.SetModeAsync(first, AssistantMode.Translate, CancellationToken.None));
        Value(await repository.SetTemperatureAsync(first, new Temperature(0.7m), CancellationToken.None));
        await TurnAsync(storage, first, "Первый", "Ответ");
        await TurnAsync(storage, second, "Второй", "Ответ второго");
        var pending = Value(await repository.BeginTurnAsync(first, new LlmText("В ожидании"), CancellationToken.None));

        // Act
        var reset = Value(await repository.ResetAsync(first, CancellationToken.None));

        // Assert
        Assert.Equal(AssistantMode.Translate, reset.Mode);
        Assert.Equal(new Temperature(0.7m), reset.Temperature);
        Assert.Empty(Value(await repository.LoadAsync(first, CancellationToken.None)).History);
        Assert.Equal(2, Value(await repository.LoadAsync(second, CancellationToken.None)).History.Count);
        await using var remaining = storage.Source.CreateCommand("SELECT count(*) FROM conversation_turns WHERE chat_id = 42");
        Assert.Equal(0L, await remaining.ExecuteScalarAsync());
        var late = Assert.IsType<ConversationResult<ConversationTurnStatus>.Failure>(await repository.CompleteTurnAsync(first, pending.Id, new LlmText("Поздний ответ"), CancellationToken.None));
        Assert.Equal(ErrorCode.ConversationTurnNotFound, late.Error.Code);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task SwitchingModeClearsHistoryAndRejectsOldAnswer()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        var chat = new ChatId(42);
        Value(await repository.SetTemperatureAsync(chat, new Temperature(0.7m), CancellationToken.None));
        await TurnAsync(storage, chat, "Старый вопрос", "Старый ответ");
        var pending = Value(await repository.BeginTurnAsync(chat, new LlmText("Ещё вопрос"), CancellationToken.None));

        var changed = Value(await repository.SetModeAsync(chat, AssistantMode.Translate, CancellationToken.None));

        // Act
        var late = await repository.CompleteTurnAsync(chat, pending.Id, new LlmText("Запоздалый ответ"), CancellationToken.None);

        // Assert
        Assert.Equal(AssistantMode.Translate, changed.Mode);
        Assert.Equal(new Temperature(0.7m), changed.Temperature);
        Assert.Equal(1L, changed.Revision.Value);
        Assert.IsType<ConversationResult<ConversationTurnStatus>.Failure>(late);
        Assert.Empty(Value(await repository.LoadAsync(chat, CancellationToken.None)).History);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task DuplicateCompletionAndOtherUsersTurnAreRejected()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        var chat = new ChatId(42);
        var turn = Value(await repository.BeginTurnAsync(chat, new LlmText("Вопрос"), CancellationToken.None));

        var alien = await repository.CompleteTurnAsync(new ChatId(43), turn.Id, new LlmText("Чужой ответ"), CancellationToken.None);
        Value(await repository.CompleteTurnAsync(chat, turn.Id, new LlmText("Ответ"), CancellationToken.None));

        // Act
        var repeated = await repository.CompleteTurnAsync(chat, turn.Id, new LlmText("Дубликат"), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ConversationTurnNotFound, Assert.IsType<ConversationResult<ConversationTurnStatus>.Failure>(alien).Error.Code);
        Assert.Equal(ErrorCode.ConversationTurnAlreadyFinished, Assert.IsType<ConversationResult<ConversationTurnStatus>.Failure>(repeated).Error.Code);
        Assert.Equal(2, Value(await repository.LoadAsync(chat, CancellationToken.None)).History.Count);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task UserTextIsParameterizedAndDoesNotExecuteSql()
    {
        // Arrange
        await using var storage = await StorageAsync();
        const string text = "'); DROP TABLE conversation_users; --\n$текст ' с кавычками 👋";
        await TurnAsync(storage, new ChatId(42), text, "Ответ");

        // Act
        var snapshot = Value(await storage.CreateRepository().LoadAsync(new ChatId(42), CancellationToken.None));

        // Assert
        Assert.Equal(text, snapshot.History[0].Content.Value);
        Assert.Equal(AssistantMode.Study, snapshot.State.Mode);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentResetAndCompletionCannotRestoreClearedHistory()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        var chat = new ChatId(42);
        var turn = Value(await repository.BeginTurnAsync(chat, new LlmText("Вопрос"), CancellationToken.None));

        await Task.WhenAll(
            repository.CompleteTurnAsync(chat, turn.Id, new LlmText("Ответ"), CancellationToken.None),
            repository.ResetAsync(chat, CancellationToken.None));

        // Act
        var actualResult = Value(await repository.LoadAsync(chat, CancellationToken.None)).History;

        // Assert
        Assert.Empty(actualResult);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task CallerCancellationPropagates()
    {
        // Arrange
        await using var storage = await StorageAsync();
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();

        // Act
        Func<Task> act = () => storage.CreateRepository().LoadAsync(new ChatId(42), stopping.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task LoadingPrunesOldAttemptsAndMessagesWithoutChangingOtherChatsOrSettings()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        var chat = new ChatId(42);
        Value(await repository.SetModeAsync(chat, AssistantMode.Translate, CancellationToken.None));
        Value(await repository.SetTemperatureAsync(chat, new Temperature(0.7m), CancellationToken.None));
        await SeedAttemptsAsync(storage, chat, 102, mixedStatuses: true);
        await TurnAsync(storage, new ChatId(43), "Другой запрос", "Другой ответ");

        // Act
        var snapshot = Value(await repository.LoadAsync(chat, CancellationToken.None));

        // Assert
        Assert.Equal(AssistantMode.Translate, snapshot.State.Mode);
        Assert.Equal(new Temperature(0.7m), snapshot.State.Temperature);
        Assert.Equal(68, snapshot.History.Count);
        Assert.Equal("request-3", snapshot.History[0].Content.Value);
        Assert.Equal("answer-102", snapshot.History[^1].Content.Value);
        await using var count = storage.Source.CreateCommand("SELECT count(*) FROM conversation_turns WHERE chat_id = 42");
        Assert.Equal(100L, await count.ExecuteScalarAsync());
        await using var messages = storage.Source.CreateCommand("SELECT count(*) FROM conversation_messages m JOIN conversation_turns t ON t.turn_id = m.turn_id WHERE t.chat_id = 42");
        Assert.Equal(134L, await messages.ExecuteScalarAsync());
        Assert.Equal(2, Value(await repository.LoadAsync(new ChatId(43), CancellationToken.None)).History.Count);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task BeginningTurnBoundsPendingAttemptsAndKeepsNewTurnCompletable()
    {
        // Arrange
        await using var storage = await StorageAsync();
        var repository = storage.CreateRepository();
        var chat = new ChatId(42);
        Value(await repository.LoadAsync(chat, CancellationToken.None));
        await SeedAttemptsAsync(storage, chat, 100, mixedStatuses: false);
        await using var oldest = storage.Source.CreateCommand("SELECT turn_id FROM conversation_turns WHERE chat_id = 42 ORDER BY sequence LIMIT 1");
        var oldestId = new ConversationTurnId((Guid)(await oldest.ExecuteScalarAsync())!);

        // Act
        var turn = Value(await repository.BeginTurnAsync(chat, new LlmText("Новый запрос"), CancellationToken.None));
        Value(await repository.CompleteTurnAsync(chat, turn.Id, new LlmText("Новый ответ"), CancellationToken.None));

        // Assert
        await using var count = storage.Source.CreateCommand("SELECT count(*) FROM conversation_turns WHERE chat_id = 42");
        Assert.Equal(100L, await count.ExecuteScalarAsync());
        await using var messages = storage.Source.CreateCommand("SELECT count(*) FROM conversation_messages");
        Assert.Equal(101L, await messages.ExecuteScalarAsync());
        Assert.Equal(new[] { "Новый запрос", "Новый ответ" }, Value(await repository.LoadAsync(chat, CancellationToken.None)).History.Select(m => m.Content.Value));
        var late = Assert.IsType<ConversationResult<ConversationTurnStatus>.Failure>(await repository.CompleteTurnAsync(chat, oldestId, new LlmText("Поздний ответ"), CancellationToken.None));
        Assert.Equal(ErrorCode.ConversationTurnNotFound, late.Error.Code);
    }

    private async Task SeedAttemptsAsync(PostgresTestScope storage, ChatId chatId, int count, bool mixedStatuses)
    {
        await using var command = storage.Source.CreateCommand("""
            INSERT INTO conversation_turns(turn_id, chat_id, revision, status)
            SELECT md5(@chat::text || '-' || n::text)::uuid, @chat, n,
                CASE WHEN NOT @mixed THEN 'pending'
                     WHEN n % 3 = 0 THEN 'succeeded'
                     WHEN n % 3 = 1 THEN 'failed' ELSE 'pending' END
            FROM generate_series(1, @count) n ORDER BY n;
            INSERT INTO conversation_messages(turn_id, role, content)
            SELECT turn_id, 'user', 'request-' || revision::text
            FROM conversation_turns WHERE chat_id = @chat;
            INSERT INTO conversation_messages(turn_id, role, content)
            SELECT turn_id, 'assistant', 'answer-' || revision::text
            FROM conversation_turns WHERE chat_id = @chat AND status = 'succeeded';
            """);
        command.Parameters.AddWithValue("chat", chatId.Value);
        command.Parameters.AddWithValue("count", count);
        command.Parameters.AddWithValue("mixed", mixedStatuses);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<PostgresTestScope> StorageAsync()
    {
        var storage = new PostgresTestScope();
        try
        {
            await storage.InitializeAsync();
            Assert.IsType<DatabaseMigrationResult.Success>(await storage.CreateMigrator().ApplyAsync(CancellationToken.None));
            return storage;
        }
        catch
        {
            await storage.DisposeAsync();
            throw;
        }
    }

    private T Value<T>(ConversationResult<T> result)
    {
        return Assert.IsType<ConversationResult<T>.Success>(result).Value;
    }

    private async Task TurnAsync(PostgresTestScope storage, ChatId chatId, string user, string assistant)
    {
        var repository = storage.CreateRepository();
        var turn = Value(await repository.BeginTurnAsync(chatId, new LlmText(user), CancellationToken.None));
        Assert.Equal(ConversationTurnStatus.Succeeded, Value(await repository.CompleteTurnAsync(chatId, turn.Id, new LlmText(assistant), CancellationToken.None)));
    }
}
