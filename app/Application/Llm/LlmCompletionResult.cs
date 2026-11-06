using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Llm;

public abstract record class LlmCompletionResult
{
    private LlmCompletionResult()
    {
    }

    public sealed record class ToolCalls(IReadOnlyList<LlmToolCall> Calls) : LlmCompletionResult;

    public sealed record class Success(LlmResponse Response) : LlmCompletionResult;

    public sealed record class Failure(AppError Error, TimeSpan? RetryAfter = null, DateTimeOffset? QuotaResetsAt = null) : LlmCompletionResult
    {
        public string Message
        {
            get
            {
                return Error.Message;
            }
        }
    }
}
