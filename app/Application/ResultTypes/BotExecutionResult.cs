using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class BotExecutionResult
{
    private BotExecutionResult()
    {
    }

    public sealed record class Success : BotExecutionResult;

    public sealed record class Failure(AppError Error) : BotExecutionResult
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
