using ItmoBot.Application.Models;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class LlmCompletionResult
{
    private LlmCompletionResult()
    {
    }

    public sealed record class Success(LlmResponse Response) : LlmCompletionResult;

    public sealed record class Failure(AppError Error, TimeSpan? RetryAfter = null) : LlmCompletionResult
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
