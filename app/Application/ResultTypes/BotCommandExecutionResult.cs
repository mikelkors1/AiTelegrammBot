using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class BotCommandExecutionResult
{
    private BotCommandExecutionResult()
    {
    }

    public sealed record class Success(LlmText Reply) : BotCommandExecutionResult;
    public sealed record class Failure(AppError Error) : BotCommandExecutionResult;
}
