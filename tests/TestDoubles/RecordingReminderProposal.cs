using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Reminders;
using ItmoBot.Validation;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class RecordingReminderProposal : IReminderProposalHandler
{
    public int Calls
    {
        get; private set;
    }
    public Task<AgentCompletionResult> PrepareAsync(LlmToolCall call, AgentTurnContext? context, UserTimeZone? timezone, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult<AgentCompletionResult>(new AgentCompletionResult.Success(new LlmResponse(RequestChecks.LlmText("Подтвердите действие"), RequestChecks.ModelName("test/model"), null, null)));
    }
}
