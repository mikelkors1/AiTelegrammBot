using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Llm;

namespace ItmoBot.Application.AgentLoop;

public interface IAgentAuditWriter
{
    Task RecordAsync(AgentTurnContext? context, Guid request, AuditAction action, AuditStatus status, LlmToolCall? call, CancellationToken cancellationToken);
}
