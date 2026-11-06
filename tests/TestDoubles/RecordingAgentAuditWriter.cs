using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Llm;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class RecordingAgentAuditWriter : IAgentAuditWriter
{
    public readonly List<AuditStatus> Statuses = [];
    public readonly List<AuditAction> Actions = [];

    public Task RecordAsync(AgentTurnContext? context, Guid request, AuditAction action, AuditStatus status, LlmToolCall? call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Statuses.Add(status);
        Actions.Add(action);
        return Task.CompletedTask;
    }
}
