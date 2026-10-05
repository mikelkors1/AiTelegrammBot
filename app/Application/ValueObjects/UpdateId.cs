using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class UpdateId
{
    public UpdateId(int value)
    {
        if (value < 0 || value == int.MaxValue)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidUpdateId, "Идентификатор обновления должен быть неотрицательным и допускать получение следующего смещения.");
        }

        Value = value;
    }

    public int Value
    {
        get;
    }

    public UpdateOffset NextOffset()
    {
        return new UpdateOffset(Value + 1);
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
