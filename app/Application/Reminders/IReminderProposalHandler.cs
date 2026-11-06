using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Llm;

namespace ItmoBot.Application.Reminders;

public interface IReminderProposalHandler
{
    Task<AgentCompletionResult> PrepareAsync(LlmToolCall call, AgentTurnContext? context, UserTimeZone? timezone, CancellationToken cancellationToken);
}
