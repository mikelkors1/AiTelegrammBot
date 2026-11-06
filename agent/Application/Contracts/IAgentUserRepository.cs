using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;

namespace ItmoBot.Application.Agent.Contracts;

public interface IAgentUserRepository
{
    Task<AgentStorageResult<UserTimeZone?>> GetTimeZoneAsync(long owner, CancellationToken cancellationToken);
    Task<AgentStorageResult<UserTimeZone>> SetTimeZoneAsync(long owner, UserTimeZone timezone, CancellationToken cancellationToken);
}
