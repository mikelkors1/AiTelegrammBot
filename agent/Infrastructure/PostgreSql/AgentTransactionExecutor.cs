using System.Text.Json;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Infrastructure.PostgreSql.Agent.Contracts;
using ItmoBot.Shared.Errors;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Agent;

public sealed class AgentTransactionExecutor(NpgsqlDataSource source, ILogger logger) : IAgentTransactionExecutor
{
    public async Task<AgentStorageResult<T>> ExecuteAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<AgentStorageResult<T>>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var connection = await source.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var result = await operation(connection, transaction, cancellationToken);
            // Ожидаемый отказ может сопровождаться сохранением состояния expired.
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NpgsqlException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or IOException or TimeoutException or OperationCanceledException or ArgumentException or InvalidOperationException or JsonException or OverflowException or KeyNotFoundException)
        {
            var error = exception switch
            {
                PostgresException { SqlState: "40001" or "40P01" } => new AppError(ErrorCode.DatabaseOperationFailed, "Конкурирующая операция. Повторите запрос.", true),
                PostgresException => new AppError(ErrorCode.DatabaseOperationFailed, "Не удалось сохранить данные агента."),
                TimeoutException or OperationCanceledException or NpgsqlException { InnerException: TimeoutException } => new AppError(ErrorCode.DatabaseTimeout, "Истекло время ожидания хранилища.", true),
                NpgsqlException or IOException => new AppError(ErrorCode.DatabaseUnavailable, "Хранилище недоступно.", true),
                _ => new AppError(ErrorCode.InvalidAgentData, "Хранилище вернуло некорректные данные агента."),
            };
            logger.LogWarning("Операция хранилища агента завершилась ошибкой {Code}.", error.Code);
            return new AgentStorageResult<T>.Failure(error);
        }
    }
}
