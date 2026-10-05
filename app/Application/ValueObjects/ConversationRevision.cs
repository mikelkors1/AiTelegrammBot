using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class ConversationRevision
{
    public static readonly ConversationRevision Zero = new(0);

    public ConversationRevision(long value)
    {
        if (value < 0)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidConversationState, "Версия контекста не может быть отрицательной.");
        }

        Value = value;
    }

    public long Value
    {
        get;
    }
}
