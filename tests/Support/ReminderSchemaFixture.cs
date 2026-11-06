using ItmoBot.Shared.Results;
using Npgsql;
using Xunit;

namespace ItmoBot.Tests.Support;

internal sealed class ReminderSchemaFixture : IAsyncDisposable
{
    public ReminderSchemaFixture()
    {
        Storage = new PostgresTestScope();
        ActionId = Guid.NewGuid();
    }

    public PostgresTestScope Storage
    {
        get;
    }

    public Guid ActionId
    {
        get;
    }

    public async Task InitializeAsync()
    {
        await Storage.InitializeAsync();
        Assert.IsType<Result<int>.Success>(await Storage.CreateMigrator().ApplyAsync(CancellationToken.None));
        await ExecuteAsync("INSERT INTO agent_users(owner_id, timezone) VALUES (42, 'Europe/Moscow'), (84, NULL)");
        await ExecuteAsync("""
            INSERT INTO prepared_actions(action_id, owner_id, chat_id, update_id, reminder_text,
                remind_at, timezone, arguments_hash, created_at, expires_at)
            VALUES ($1, 42, 42, 100, 'Проверить расписание', '2030-01-02T15:00:00Z',
                'Europe/Moscow', $2, '2030-01-01T09:00:00Z', '2030-01-01T09:05:00Z')
            """, ActionId, new byte[32]);
    }

    public Task<int> ConfirmAsync()
    {
        return ExecuteAsync("""
            INSERT INTO action_confirmations(action_id, owner_id, chat_id, confirmation_update_id, confirmed_at)
            VALUES ($1, 42, 42, 101, '2030-01-01T09:01:00Z')
            """, ActionId);
    }

    public Task<int> CreateReminderAsync(long owner = 42)
    {
        return ExecuteAsync("""
            INSERT INTO reminders(reminder_id, action_id, owner_id, chat_id, reminder_text,
                remind_at, timezone, created_at)
            VALUES ($1, $2, $3, $3, 'Проверить расписание', '2030-01-02T15:00:00Z',
                'Europe/Moscow', '2030-01-01T09:01:00Z')
            """, Guid.NewGuid(), ActionId, owner);
    }

    public async Task<int> ExecuteAsync(string sql, params object[] values)
    {
        await using var command = Storage.Source.CreateCommand(sql);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Storage.DisposeAsync();
    }
}
