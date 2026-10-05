using ItmoBot.Shared.Errors;

namespace ItmoBot.Hosting.ResultTypes;

public abstract record class CommandLineParseResult
{
    private CommandLineParseResult()
    {
    }

    public sealed record class Success(ItmoBot.Hosting.CommandLineOptions Options) : CommandLineParseResult;

    public sealed record class Failure(AppError Error) : CommandLineParseResult
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
