using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Infrastructure.PostgreSql.Contracts;
using ItmoBot.Shared.Errors;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Migrations;

public sealed class PostgresDatabaseMigrator(
    NpgsqlDataSource source,
    ISqlMigrationCatalog catalog,
    IPostgresErrorMapper errorMapper,
    ILogger logger) : IDatabaseMigrator
{
    public async Task<DatabaseMigrationResult> ApplyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await ApplyCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NpgsqlException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or OperationCanceledException or IOException or InvalidOperationException)
        {
            var mapped = error is InvalidOperationException
                ? new AppError(ErrorCode.DatabaseMigrationFailed, "Не удалось прочитать встроенные миграции приложения.")
                : errorMapper.Map(error);
            logger.LogWarning("Миграция БД завершилась ошибкой {Code}.", mapped.Code);
            return new DatabaseMigrationResult.Failure(mapped);
        }
    }

    private async Task<DatabaseMigrationResult> ApplyCoreAsync(CancellationToken cancellationToken)
    {
        var migrations = catalog.Read();
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InitializeLedgerAsync(connection, transaction, cancellationToken);
        var applied = await ReadAppliedAsync(connection, transaction, cancellationToken);
        if (applied.Keys.Any(version => migrations.All(migration => migration.Version != version)))
        {
            return Mismatch();
        }

        var count = 0;
        foreach (var migration in migrations)
        {
            if (applied.TryGetValue(migration.Version, out var checksum))
            {
                if (checksum != migration.Checksum)
                {
                    return Mismatch();
                }

                continue;
            }

            await ApplyMigrationAsync(connection, transaction, migration, cancellationToken);
            count++;
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Миграции БД завершены: применено {Count}.", count);
        return new DatabaseMigrationResult.Success(count);
    }

    private async Task InitializeLedgerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        // Transaction-scoped lock serializes migration runners on the same database.
        await using var command = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(176000001);
            CREATE TABLE IF NOT EXISTS bot_schema_migrations (
                version integer PRIMARY KEY,
                name text NOT NULL,
                checksum text NOT NULL,
                applied_at timestamptz NOT NULL DEFAULT now()
            );
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<Dictionary<int, string>> ReadAppliedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT version, checksum FROM bot_schema_migrations ORDER BY version", connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var applied = new Dictionary<int, string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetInt32(0), reader.GetString(1));
        }

        return applied;
    }

    private async Task ApplyMigrationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, SqlMigration migration, CancellationToken cancellationToken)
    {
        await using var schema = new NpgsqlCommand(migration.Sql, connection, transaction);
        await schema.ExecuteNonQueryAsync(cancellationToken);
        await using var record = new NpgsqlCommand("INSERT INTO bot_schema_migrations(version, name, checksum) VALUES (@version, @name, @checksum)", connection, transaction);
        record.Parameters.AddWithValue("version", migration.Version);
        record.Parameters.AddWithValue("name", migration.Name);
        record.Parameters.AddWithValue("checksum", migration.Checksum);
        await record.ExecuteNonQueryAsync(cancellationToken);
    }

    private DatabaseMigrationResult Mismatch()
    {
        return new DatabaseMigrationResult.Failure(new AppError(ErrorCode.DatabaseMigrationMismatch,
            "История миграций не соответствует этой версии приложения. Проверьте версии и контрольные суммы; данные не удалялись."));
    }
}
