using ItmoBot.Shared.Errors;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.ResultTypes;

public abstract record class TelegramSendResult
{
    private TelegramSendResult()
    {
    }

    public sealed record class Success(MessageId MessageId) : TelegramSendResult;

    public sealed record class Failure(AppError Error) : TelegramSendResult
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
