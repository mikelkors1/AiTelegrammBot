using ItmoBot.Shared.Errors;

namespace ItmoBot.Tools.ResultTypes;

public abstract record class CommandExecutionResult
{
    private CommandExecutionResult()
    {
    }

    public sealed record class Success : CommandExecutionResult;

    public sealed record class Failure(AppError Error) : CommandExecutionResult;
}
