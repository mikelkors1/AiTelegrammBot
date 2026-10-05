using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class ReadinessCheckResult
{
    private ReadinessCheckResult()
    {
    }

    public sealed record class Success : ReadinessCheckResult;

    public sealed record class Failure(AppError Error) : ReadinessCheckResult
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
