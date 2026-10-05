using ItmoBot.Application.Contracts;
using ItmoBot.Infrastructure.PostgreSql;
using ItmoBot.Infrastructure.PostgreSql.Conversations;
using ItmoBot.Infrastructure.PostgreSql.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace ItmoBot.Tests.Support;

internal sealed class PostgresTestScope : IAsyncDisposable
{
    private readonly string schema;
    private readonly string connectionString;

    public NpgsqlDataSource Source
    {
        get;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var command = Source.CreateCommand($"CREATE SCHEMA \"{schema}\"");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public IDatabaseMigrator CreateMigrator()
    {
        return new PostgresDatabaseMigrator(Source, new SqlMigrationCatalog(), new PostgresErrorMapper(), NullLogger.Instance);
    }

    public IConversationRepository CreateRepository(NpgsqlDataSource? source = null)
    {
        return new PostgresConversationRepository(
            new PostgresOperationExecutor(source ?? Source, new PostgresErrorMapper(), NullLogger.Instance),
            new ConversationStateStore(), new ConversationTurnStore(), new ConversationHistoryStore());
    }

    public NpgsqlDataSource CreateNewSource()
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schema,
        };
        return NpgsqlDataSource.Create(builder.ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        await Source.DisposeAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    public PostgresTestScope()
    {
        schema = "lab1_test_" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("TEST_POSTGRES_HOST") ?? "127.0.0.1",
            Port = int.Parse(Environment.GetEnvironmentVariable("TEST_POSTGRES_PORT") ?? "55432"),
            Database = "bot_test",
            Username = "bot_test",
            Password = Environment.GetEnvironmentVariable("TEST_POSTGRES_PASSWORD") ?? "test-only-password",
            Timeout = 5,
            CommandTimeout = 5,
        };
        connectionString = builder.ConnectionString;
        builder.SearchPath = schema;
        Source = NpgsqlDataSource.Create(builder.ConnectionString);
    }
}
