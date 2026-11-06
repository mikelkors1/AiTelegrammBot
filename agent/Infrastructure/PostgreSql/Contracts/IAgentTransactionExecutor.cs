using ItmoBot.Application.Agent.ResultTypes;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Agent.Contracts;

public interface IAgentTransactionExecutor
{
    Task<AgentStorageResult<T>> ExecuteAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<AgentStorageResult<T>>> operation, CancellationToken cancellationToken);
}
