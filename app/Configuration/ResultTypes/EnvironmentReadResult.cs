using ItmoBot.Shared.Errors;

namespace ItmoBot.Configuration.ResultTypes;

public abstract record class EnvironmentReadResult
{
    private EnvironmentReadResult()
    {
    }

    public sealed record class Success(IReadOnlyDictionary<string, string> Values) : EnvironmentReadResult;

    public sealed record class Failure(AppError Error) : EnvironmentReadResult
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
