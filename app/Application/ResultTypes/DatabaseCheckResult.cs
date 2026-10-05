using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class DatabaseCheckResult
{
    private DatabaseCheckResult()
    {
    }

    public sealed record class Success : DatabaseCheckResult;

    public sealed record class Failure(AppError Error) : DatabaseCheckResult
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
