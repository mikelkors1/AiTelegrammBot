using ItmoBot.Infrastructure.PostgreSql.Migrations;
using ItmoBot.Infrastructure.PostgreSql;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Integration.Storage;

public sealed class DatabaseMigrationIntegrationTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_RepeatedMigration_KeepsExistingData()
    {
        // Arrange
        await using var storage = new PostgresTestScope();
        await storage.InitializeAsync();

        // Act
        var actualResult = Assert.IsType<Result<int>.Success>(await storage.CreateMigrator().ApplyAsync(CancellationToken.None)).Value;

        // Assert
        Assert.Equal(new SqlMigrationCatalog().Read().Count, actualResult);
        Assert.IsType<Result<ItmoBot.Application.Conversations.ConversationState>.Success>(await storage.CreateRepository().SetTemperatureAsync(AgentChecks.ChatId(42), RequestChecks.Temperature(1m), CancellationToken.None));

        Assert.Equal(0, Assert.IsType<Result<int>.Success>(await storage.CreateMigrator().ApplyAsync(CancellationToken.None)).Value);
        var snapshot = Assert.IsType<Result<ItmoBot.Application.Conversations.ConversationSnapshot>.Success>(await storage.CreateRepository().LoadAsync(AgentChecks.ChatId(42), CancellationToken.None));
        Assert.Equal(RequestChecks.Temperature(1m), snapshot.Value.State.Temperature);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_ConcurrentRunners_AppliesEachMigrationOnce()
    {
        // Arrange
        await using var storage = new PostgresTestScope();
        await storage.InitializeAsync();

        // Act
        var results = await Task.WhenAll(storage.CreateMigrator().ApplyAsync(CancellationToken.None), storage.CreateMigrator().ApplyAsync(CancellationToken.None));

        // Assert
        Assert.Equal(new[] { 0, new SqlMigrationCatalog().Read().Count }, results.Select(result => Assert.IsType<Result<int>.Success>(result).Value).Order());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_ChangedChecksum_StopsMigrationWithoutRemovingData()
    {
        // Arrange
        await using var storage = new PostgresTestScope();
        await storage.InitializeAsync();

        // Act
        var actualResult = await storage.CreateMigrator().ApplyAsync(CancellationToken.None);

        // Assert
        Assert.IsType<Result<int>.Success>(actualResult);
        await storage.CreateRepository().LoadAsync(AgentChecks.ChatId(42), CancellationToken.None);
        await using var alter = storage.Source.CreateCommand("UPDATE bot_schema_migrations SET checksum = 'changed'");
        await alter.ExecuteNonQueryAsync();

        var failure = Assert.IsType<Result<int>.Failure>(await storage.CreateMigrator().ApplyAsync(CancellationToken.None));

        Assert.Equal(ErrorCode.DatabaseMigrationMismatch, failure.Error.Code);
        await using var count = storage.Source.CreateCommand("SELECT count(*) FROM conversation_users");
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ApplyAsync_SqlFailure_RollsBackWholeMigrationBatch()
    {
        // Arrange
        await using var storage = new PostgresTestScope();
        await storage.InitializeAsync();
        var migrations = new SqlMigrationCatalog().Read().Concat([new SqlMigration(new SqlMigrationCatalog().Read().Max(migration => migration.Version) + 1, "broken", "SELECT * FROM missing_test_table", "test")]).ToArray();
        var migrator = new PostgresDatabaseMigrator(storage.Source, new FixedMigrationCatalog(migrations), new PostgresErrorMapper(), NullLogger.Instance);

        // Act
        var actualResult = await migrator.ApplyAsync(CancellationToken.None);

        // Assert
        Assert.IsType<Result<int>.Failure>(actualResult);
        await using var tables = storage.Source.CreateCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema()");
        Assert.Equal(0L, await tables.ExecuteScalarAsync());
        Assert.Equal(new SqlMigrationCatalog().Read().Count, Assert.IsType<Result<int>.Success>(await storage.CreateMigrator().ApplyAsync(CancellationToken.None)).Value);
    }
}
