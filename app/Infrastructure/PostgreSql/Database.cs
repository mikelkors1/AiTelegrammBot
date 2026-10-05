using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Configuration.Models;
using ItmoBot.Shared.Errors;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql;

public sealed class Database : IDatabase
{
    private readonly ILogger? logger;

    public Database(Settings settings, ILogger? logger = null)
    {
        this.logger = logger;
        DataSource = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder
        {
            Host = settings.PostgresHost.Value,
            Port = settings.PostgresPort.Value,
            Database = settings.PostgresDb.Value,
            Username = settings.PostgresUser.Value,
            Password = settings.PostgresPassword.Value,
            MinPoolSize = 1,
            MaxPoolSize = 5,
            Timeout = 5,
            CommandTimeout = 5,
            IncludeErrorDetail = false,
            // This deployment uses password authentication, without Kerberos.
            GssEncryptionMode = GssEncryptionMode.Disable,
        }.ConnectionString);
    }

    public NpgsqlDataSource DataSource
    {
        get;
    }

    public async Task<DatabaseCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            await using var command = DataSource.CreateCommand("SELECT 1");

            if (await command.ExecuteScalarAsync(timeout.Token) is not int value || value != 1)
            {
                return new DatabaseCheckResult.Failure(new AppError(
                    ErrorCode.DatabaseUnexpectedResponse, "PostgreSQL вернул неожиданный результат SELECT 1."));
            }

            return new DatabaseCheckResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NpgsqlException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (PostgresException error) when (error.SqlState is "28P01" or "28000")
        {
            logger?.LogWarning(error, "PostgreSQL отклонил авторизацию.");
            return new DatabaseCheckResult.Failure(new AppError(
                ErrorCode.DatabaseAuthenticationFailed,
                "PostgreSQL отклонил авторизацию. Проверьте пользователя и пароль; изменение env-файла не меняет пароль существующей роли."));
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            logger?.LogWarning(error, "Проверка PostgreSQL завершилась ошибкой.");
            if (timeout.IsCancellationRequested || error is TimeoutException)
            {
                return new DatabaseCheckResult.Failure(new AppError(
                    ErrorCode.DatabaseTimeout, "Истекло время ожидания PostgreSQL. Проверьте подключение к БД.", IsRetryable: true));
            }

            return new DatabaseCheckResult.Failure(new AppError(
                ErrorCode.DatabaseUnavailable, "PostgreSQL недоступен. Проверьте адрес, порт и запуск контейнера БД.", IsRetryable: true));
        }
    }

    public ValueTask DisposeAsync()
    {
        return DataSource.DisposeAsync();
    }
}
