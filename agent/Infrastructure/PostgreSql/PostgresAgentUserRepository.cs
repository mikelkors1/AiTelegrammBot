using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Infrastructure.PostgreSql.Agent.Contracts;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Agent;

public sealed class PostgresAgentUserRepository(IAgentTransactionExecutor executor, AgentUserStore users,
    PreparedActionStore actions) : IAgentUserRepository
{
    public Task<AgentStorageResult<UserTimeZone?>> GetTimeZoneAsync(long owner, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<UserTimeZone?>(async (connection, transaction, token) =>
        {
            var timezone = await users.LockUserAsync(connection, transaction, owner, token);
            return new AgentStorageResult<UserTimeZone?>.Success(timezone);
        }, cancellationToken);
    }

    public Task<AgentStorageResult<UserTimeZone>> SetTimeZoneAsync(long owner, UserTimeZone timezone, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync<UserTimeZone>(async (connection, transaction, token) =>
        {
            var previous = await users.LockUserAsync(connection, transaction, owner, token);
            if (previous?.Value.Id != timezone.Value.Id)
            {
                await actions.CancelPendingAsync(connection, transaction, owner, token);
            }

            await using var command = new NpgsqlCommand("UPDATE agent_users SET timezone = $2 WHERE owner_id = $1", connection, transaction);
            command.Parameters.AddWithValue(owner);
            command.Parameters.AddWithValue(timezone.Value.Id);
            await command.ExecuteNonQueryAsync(token);
            return new AgentStorageResult<UserTimeZone>.Success(timezone);
        }, cancellationToken);
    }
}
