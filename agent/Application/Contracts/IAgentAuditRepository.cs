using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;

namespace ItmoBot.Application.Agent.Contracts;

public interface IAgentAuditRepository
{
    Task<AgentStorageResult<AgentAuditEvent>> AppendEventAsync(AgentAuditEvent entry, CancellationToken cancellationToken);
    Task<AgentStorageResult<AgentAuditEvent>> GetLatestEventAsync(long owner, long chat, CancellationToken cancellationToken);
}
