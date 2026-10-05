using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class MessageHandlingResult
{
    private MessageHandlingResult()
    {
    }

    public sealed record class Success(bool WasHandled) : MessageHandlingResult;

    public sealed record class Failure(AppError Error) : MessageHandlingResult
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
