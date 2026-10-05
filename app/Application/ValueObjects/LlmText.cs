using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class LlmText
{
    public LlmText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidLlmText, "Текст для языковой модели не должен быть пустым.");
        }

        Value = value;
    }

    public string Value
    {
        get;
    }

    public override string ToString()
    {
        return Value;
    }
}
