using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class TelegramDeleteResult
{
    private TelegramDeleteResult()
    {
    }

    public sealed record class Success : TelegramDeleteResult;

    public sealed record class Failure(AppError Error) : TelegramDeleteResult;
}
