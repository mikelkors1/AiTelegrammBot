using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class ConversationResult<T>
{
    private ConversationResult()
    {
    }

    public sealed record class Success(T Value) : ConversationResult<T>;

    public sealed record class Failure(AppError Error) : ConversationResult<T>;
}
