using ItmoBot.Shared.Errors;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.ResultTypes;

public abstract record class TelegramInitializationResult
{
    private TelegramInitializationResult()
    {
    }

    public sealed record class Success(BotUsername Username) : TelegramInitializationResult;

    public sealed record class Failure(AppError Error) : TelegramInitializationResult
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
