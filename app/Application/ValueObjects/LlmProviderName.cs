using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class LlmProviderName
{
    public LlmProviderName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidLlmResponse, "API вернул некорректное имя провайдера.");
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
