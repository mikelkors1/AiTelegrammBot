using ItmoBot.Application.Agent.Models;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Agent;

public sealed class AgentUserStore
{
    public async Task<UserTimeZone?> LockUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long owner, CancellationToken cancellationToken)
    {
        await using var create = new NpgsqlCommand("INSERT INTO agent_users(owner_id) VALUES ($1) ON CONFLICT DO NOTHING", connection, transaction);
        create.Parameters.AddWithValue(owner);
        await create.ExecuteNonQueryAsync(cancellationToken);
        await using var read = new NpgsqlCommand("SELECT timezone FROM agent_users WHERE owner_id = $1 FOR UPDATE", connection, transaction);
        read.Parameters.AddWithValue(owner);
        var value = await read.ExecuteScalarAsync(cancellationToken);
        return value is string timezone ? new UserTimeZone(timezone) : null;
    }
}
