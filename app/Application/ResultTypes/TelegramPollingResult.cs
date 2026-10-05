using ItmoBot.Application.Models;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class TelegramPollingResult
{
    private TelegramPollingResult()
    {
    }

    public sealed record class Success(IReadOnlyList<IncomingUpdate> Updates) : TelegramPollingResult;

    public sealed record class Failure(AppError Error) : TelegramPollingResult
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
