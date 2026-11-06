using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.AgentLoop;

public abstract record class AgentCompletionResult
{
    private AgentCompletionResult()
    {
    }
    public sealed record class Success(LlmResponse Response, PreparedAction? PendingAction = null) : AgentCompletionResult;
    public sealed record class Failure(AppError Error) : AgentCompletionResult;
}
