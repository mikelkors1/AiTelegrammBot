using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Commands;

public abstract record class BotCommandParseResult
{
    private BotCommandParseResult()
    {
    }

    public sealed record class NotCommand : BotCommandParseResult;
    public sealed record class Success(BotCommand Command) : BotCommandParseResult;
    public sealed record class Failure(AppError Error) : BotCommandParseResult;
}
