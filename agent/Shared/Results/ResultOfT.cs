using ItmoBot.Shared.Errors;

namespace ItmoBot.Shared.Results;

public abstract record class Result<T>
{
    private Result()
    {
    }

    public sealed record class Success(T Value) : Result<T>;

    public sealed record class Failure(AppError Error) : Result<T>
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
