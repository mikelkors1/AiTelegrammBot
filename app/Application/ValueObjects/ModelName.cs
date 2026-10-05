using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class ModelName
{
    public ModelName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidModelName, "LLM_MODEL: укажите имя модели без пробелов и управляющих символов.");
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
