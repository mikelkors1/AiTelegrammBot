using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class StubAgentUsers : IAgentUserRepository
{
    public Task<AgentStorageResult<UserTimeZone?>> GetTimeZoneAsync(long owner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<AgentStorageResult<UserTimeZone?>>(new AgentStorageResult<UserTimeZone?>.Success(new UserTimeZone("Europe/Moscow")));
    }

    public Task<AgentStorageResult<UserTimeZone>> SetTimeZoneAsync(long owner, UserTimeZone timezone, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<AgentStorageResult<UserTimeZone>>(new AgentStorageResult<UserTimeZone>.Success(timezone));
    }
}
