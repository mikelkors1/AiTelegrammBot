using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class ConversationTurnId
{
    public ConversationTurnId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidConversationState, "Идентификатор хода диалога не должен быть пустым.");
        }

        Value = value;
    }

    public Guid Value
    {
        get;
    }
}
