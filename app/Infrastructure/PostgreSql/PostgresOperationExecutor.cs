using ItmoBot.Application.ResultTypes;
using ItmoBot.Infrastructure.PostgreSql.Contracts;
using ItmoBot.Shared.Exceptions;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql;

public sealed class PostgresOperationExecutor(NpgsqlDataSource source, IPostgresErrorMapper errorMapper, ILogger logger) : IPostgresOperationExecutor
{
    public async Task<ConversationResult<T>> ExecuteAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<ConversationResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var connection = await source.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var result = await operation(connection, transaction, cancellationToken);
            if (result is ConversationResult<T>.Success)
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
            or IOException or ValueObjectValidationException or InvalidOperationException)
        {
            var mapped = errorMapper.Map(error);
            logger.LogWarning("Операция хранилища диалога завершилась ошибкой {Code}.", mapped.Code);
            return new ConversationResult<T>.Failure(mapped);
        }
    }
}
