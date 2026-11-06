using ItmoBot.Shared.Exceptions;
using ItmoBot.Shared.Results;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql;

public sealed class PostgresOperationExecutor(NpgsqlDataSource source, PostgresErrorMapper errorMapper, ILogger logger)
{
    public async Task<Result<T>> ExecuteAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<Result<T>>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var connection = await source.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var result = await operation(connection, transaction, cancellationToken);
            if (result is Result<T>.Success)
            {
                await transaction.CommitAsync(cancellationToken);
            }

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
        catch (Exception error) when (error is NpgsqlException or TimeoutException or OperationCanceledException
            or IOException or InputValidationException or InvalidOperationException)
        {
            var mapped = errorMapper.Map(error);
            logger.LogWarning("Операция хранилища диалога завершилась ошибкой {Code}.", mapped.Code);
            return new Result<T>.Failure(mapped);
        }
    }
}
