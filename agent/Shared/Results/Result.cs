using ItmoBot.Shared.Errors;

namespace ItmoBot.Shared.Results;

public abstract record class Result
{
    private Result()
    {
    }

    public sealed record class Success : Result;

    public sealed record class Failure(AppError Error) : Result
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
