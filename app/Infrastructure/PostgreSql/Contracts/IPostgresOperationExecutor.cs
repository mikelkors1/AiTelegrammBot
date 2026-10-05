using ItmoBot.Application.ResultTypes;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Contracts;

public interface IPostgresOperationExecutor
{
    Task<ConversationResult<T>> ExecuteAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<ConversationResult<T>>> operation,
        CancellationToken cancellationToken);
}
