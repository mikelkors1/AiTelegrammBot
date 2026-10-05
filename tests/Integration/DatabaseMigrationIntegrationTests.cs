using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Infrastructure.PostgreSql;
using ItmoBot.Infrastructure.PostgreSql.Migrations;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Integration;

public sealed class DatabaseMigrationIntegrationTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task RepeatedMigrationKeepsExistingData()
    {
        // Arrange
        await using var storage = new PostgresTestScope();
        await storage.InitializeAsync();

        // Act
        var actualResult = Assert.IsType<DatabaseMigrationResult.Success>(await storage.CreateMigrator().ApplyAsync(CancellationToken.None)).AppliedCount;

        // Assert
        Assert.Equal(1, actualResult);
        Assert.IsType<ConversationResult<ItmoBot.Application.Models.ConversationState>.Success>(await storage.CreateRepository().SetTemperatureAsync(new ChatId(42), new Temperature(1m), CancellationToken.None));

        Assert.Equal(0, Assert.IsType<DatabaseMigrationResult.Success>(await storage.CreateMigrator().ApplyAsync(CancellationToken.None)).AppliedCount);
        var snapshot = Assert.IsType<ConversationResult<ItmoBot.Application.Models.ConversationSnapshot>.Success>(await storage.CreateRepository().LoadAsync(new ChatId(42), CancellationToken.None));
        Assert.Equal(new Temperature(1m), snapshot.Value.State.Temperature);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentMigrationRunnersApplyScriptOnce()
    {
        // Arrange
        await using var storage = new PostgresTestScope();
        await storage.InitializeAsync();

        // Act
        var results = await Task.WhenAll(storage.CreateMigrator().ApplyAsync(CancellationToken.None), storage.CreateMigrator().ApplyAsync(CancellationToken.None));

        // Assert
        Assert.Equal(new[] { 0, 1 }, results.Select(result => Assert.IsType<DatabaseMigrationResult.Success>(result).AppliedCount).Order());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ChangedChecksumStopsMigrationWithoutRemovingData()
    {
        // Arrange
        await using var storage = new PostgresTestScope();
        await storage.InitializeAsync();

        // Act
        var actualResult = await storage.CreateMigrator().ApplyAsync(CancellationToken.None);

        // Assert
        Assert.IsType<DatabaseMigrationResult.Success>(actualResult);
        await storage.CreateRepository().LoadAsync(new ChatId(42), CancellationToken.None);
        await using var alter = storage.Source.CreateCommand("UPDATE bot_schema_migrations SET checksum = 'changed'");
        await alter.ExecuteNonQueryAsync();

        var failure = Assert.IsType<DatabaseMigrationResult.Failure>(await storage.CreateMigrator().ApplyAsync(CancellationToken.None));

        Assert.Equal(ErrorCode.DatabaseMigrationMismatch, failure.Error.Code);
        await using var count = storage.Source.CreateCommand("SELECT count(*) FROM conversation_users");
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task SqlFailureRollsBackWholeMigrationBatch()
    {
        // Arrange
        await using var storage = new PostgresTestScope();
        await storage.InitializeAsync();
        var migrations = new SqlMigrationCatalog().Read().Concat([new SqlMigration(2, "broken", "SELECT * FROM missing_test_table", "test")]).ToArray();
        var migrator = new PostgresDatabaseMigrator(storage.Source, new FixedMigrationCatalog(migrations), new PostgresErrorMapper(), NullLogger.Instance);

        // Act
        var actualResult = await migrator.ApplyAsync(CancellationToken.None);

        // Assert
        Assert.IsType<DatabaseMigrationResult.Failure>(actualResult);
        await using var tables = storage.Source.CreateCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema()");
        Assert.Equal(0L, await tables.ExecuteScalarAsync());
        Assert.Equal(1, Assert.IsType<DatabaseMigrationResult.Success>(await storage.CreateMigrator().ApplyAsync(CancellationToken.None)).AppliedCount);
    }
}
