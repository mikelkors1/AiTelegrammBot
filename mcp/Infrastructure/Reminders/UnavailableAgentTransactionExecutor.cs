using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Infrastructure.PostgreSql.Agent.Contracts;
using ItmoBot.Shared.Errors;
using Npgsql;

namespace ItmoBot.Mcp.Infrastructure.Reminders;

public sealed class UnavailableAgentTransactionExecutor : IAgentTransactionExecutor
{
    public Task<AgentStorageResult<T>> ExecuteAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<AgentStorageResult<T>>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<AgentStorageResult<T>>(new AgentStorageResult<T>.Failure(
            new AppError(ErrorCode.DatabaseUnavailable, "Хранилище напоминаний не настроено.", true)));
    }
}
