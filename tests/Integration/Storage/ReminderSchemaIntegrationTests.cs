using ItmoBot.Application.Conversations;
using ItmoBot.Infrastructure.PostgreSql.Migrations;
using ItmoBot.Infrastructure.PostgreSql;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace ItmoBot.Tests.Integration.Storage;

public sealed class ReminderSchemaIntegrationTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task WriteToDatabase_UpgradeFromLab1_PreservesDialogueAndAppliesOnlyNewMigration()
    {
        // Arrange
        await using var storage = new PostgresTestScope();
        await storage.InitializeAsync();
        var original = new SqlMigrationCatalog().Read().Where(migration => migration.Version == 1).ToArray();
        var originalMigrator = new PostgresDatabaseMigrator(storage.Source, new FixedMigrationCatalog(original), new PostgresErrorMapper(), NullLogger.Instance);
        Assert.IsType<Result<int>.Success>(await originalMigrator.ApplyAsync(CancellationToken.None));
        var repository = storage.CreateRepository();
        long chat = AgentChecks.ChatId(42);
        Assert.IsType<Result<ConversationState>.Success>(await repository.SetTemperatureAsync(chat, RequestChecks.Temperature(1m), CancellationToken.None));
        var turn = Assert.IsType<Result<ConversationTurn>.Success>(
            await repository.BeginTurnAsync(chat, RequestChecks.LlmText("Учебный вопрос"), CancellationToken.None)).Value;
        Assert.IsType<Result<ConversationTurnStatus>.Success>(
            await repository.CompleteTurnAsync(chat, turn.Id, RequestChecks.LlmText("Учебный ответ"), CancellationToken.None));

        // Act
        var upgrade = await storage.CreateMigrator().ApplyAsync(CancellationToken.None);
        var repeat = await storage.CreateMigrator().ApplyAsync(CancellationToken.None);
        var dialogue = await repository.LoadAsync(chat, CancellationToken.None);

        // Assert
        Assert.Equal(1, Assert.IsType<Result<int>.Success>(upgrade).Value);
        Assert.Equal(0, Assert.IsType<Result<int>.Success>(repeat).Value);
        Assert.Equal(RequestChecks.Temperature(1m), Assert.IsType<Result<ConversationSnapshot>.Success>(dialogue).Value.State.Temperature);
        Assert.Equal(new[] { "Учебный вопрос", "Учебный ответ" },
            Assert.IsType<Result<ConversationSnapshot>.Success>(dialogue).Value.History.Select(message => message.Content));
        await using var tables = storage.Source.CreateCommand("""
            SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema()
                AND table_name IN ('agent_users', 'prepared_actions', 'action_confirmations', 'reminders', 'agent_audit_events')
            """);
        Assert.Equal(5L, await tables.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task WriteToDatabase_ConcurrentPreparationOfSameUpdate_CreatesOnlyOneAction()
    {
        // Arrange
        await using var fixture = new ReminderSchemaFixture();
        await fixture.InitializeAsync();
        const string sql = """
            INSERT INTO prepared_actions(action_id, owner_id, chat_id, update_id, reminder_text,
                remind_at, timezone, arguments_hash, created_at, expires_at)
            SELECT $1, owner_id, chat_id, 200, reminder_text, remind_at, timezone, arguments_hash, created_at, expires_at
            FROM prepared_actions WHERE action_id = $2
            """;

        // Act
        var results = await Task.WhenAll(TryInsertAsync(fixture, sql, Guid.NewGuid(), fixture.ActionId),
            TryInsertAsync(fixture, sql, Guid.NewGuid(), fixture.ActionId));

        // Assert
        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result == PostgresErrorCodes.UniqueViolation);
        await using var count = fixture.Storage.Source.CreateCommand("SELECT count(*) FROM prepared_actions WHERE owner_id = 42 AND update_id = 200");
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task WriteToDatabase_Reminder_RequiresConfirmationForSameOwner()
    {
        // Arrange
        await using var fixture = new ReminderSchemaFixture();
        await fixture.InitializeAsync();

        // Act
        var unconfirmed = await Assert.ThrowsAsync<PostgresException>(() => fixture.CreateReminderAsync());
        await fixture.ConfirmAsync();
        var otherOwner = await Assert.ThrowsAsync<PostgresException>(() => fixture.CreateReminderAsync(84));
        var created = await fixture.CreateReminderAsync();

        // Assert
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, unconfirmed.SqlState);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, otherOwner.SqlState);
        Assert.Equal(1, created);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task WriteToDatabase_Confirmation_CannotReferenceAnotherOwnerAndCannotBeInsertedTwice()
    {
        // Arrange
        await using var fixture = new ReminderSchemaFixture();
        await fixture.InitializeAsync();

        // Act
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync("""
            INSERT INTO action_confirmations(action_id, owner_id, chat_id, confirmation_update_id)
            VALUES ($1, 84, 84, 101)
            """, fixture.ActionId));
        await fixture.ConfirmAsync();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => fixture.ConfirmAsync());

        // Assert
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreign.SqlState);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task WriteToDatabase_ConcurrentReminderInsertsForOneAction_CreateOnlyOneRow()
    {
        // Arrange
        await using var fixture = new ReminderSchemaFixture();
        await fixture.InitializeAsync();
        await fixture.ConfirmAsync();
        const string sql = """
            INSERT INTO reminders(reminder_id, action_id, owner_id, chat_id, reminder_text, remind_at, timezone)
            SELECT $1, action_id, owner_id, chat_id, reminder_text, remind_at, timezone
            FROM prepared_actions WHERE action_id = $2
            """;

        // Act
        var results = await Task.WhenAll(TryInsertAsync(fixture, sql, Guid.NewGuid(), fixture.ActionId),
            TryInsertAsync(fixture, sql, Guid.NewGuid(), fixture.ActionId));

        // Assert
        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result == PostgresErrorCodes.UniqueViolation);
        await using var count = fixture.Storage.Source.CreateCommand("SELECT count(*) FROM reminders");
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task WriteToDatabase_InvalidActionPayloadAndLifetime_AreRejectedByDatabase()
    {
        // Arrange
        await using var fixture = new ReminderSchemaFixture();
        await fixture.InitializeAsync();
        var changes = new[]
        {
            "reminder_text = ''", "reminder_text = '   '", "reminder_text = repeat('x', 501)",
            "arguments_hash = decode('00', 'hex')", "status = 'unknown'", "tool_name = 'get_weather'",
            "chat_id = 84", "update_id = -1", "timezone = ''", "timezone = 'Europe/Moscow bad'",
            "remind_at = created_at", "remind_at = 'infinity'::timestamptz",
            "expires_at = created_at", "expires_at = created_at + interval '6 minutes'",
        };

        // Act
        var failures = new List<PostgresException>();
        foreach (var change in changes)
        {
            failures.Add(await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync($"UPDATE prepared_actions SET {change} WHERE action_id = $1", fixture.ActionId)));
        }

        // Assert
        Assert.All(failures, failure => Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState));
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task WriteToDatabase_Audit_CanBeStoredBeforeTimezoneSelectionAndRejectsInvalidSummary()
    {
        // Arrange
        await using var fixture = new ReminderSchemaFixture();
        await fixture.InitializeAsync();
        var request = Guid.NewGuid();
        await fixture.ExecuteAsync("""
            INSERT INTO agent_audit_events(request_id, owner_id, chat_id, update_id, action, status, reason)
            VALUES ($1, 84, 84, 300, 'clarify', 'succeeded', 'Требуется часовой пояс')
            """, request);

        // Act
        var badSummary = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync("UPDATE agent_audit_events SET arguments_summary = '[]'::jsonb WHERE request_id = $1", request));
        var negativeDuration = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync("UPDATE agent_audit_events SET duration_ms = -1 WHERE request_id = $1", request));
        var missingTool = await Assert.ThrowsAsync<PostgresException>(() => fixture.ExecuteAsync("UPDATE agent_audit_events SET action = 'tool_call' WHERE request_id = $1", request));

        // Assert
        Assert.Equal(PostgresErrorCodes.CheckViolation, badSummary.SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, negativeDuration.SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, missingTool.SqlState);
        await using var count = fixture.Storage.Source.CreateCommand("SELECT count(*) FROM agent_audit_events WHERE owner_id = 84");
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    private async Task<string?> TryInsertAsync(ReminderSchemaFixture fixture, string sql, params object[] values)
    {
        try
        {
            await fixture.ExecuteAsync(sql, values);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }
}
