using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Configuration.ValueObjects;

public sealed record class LlmApiKey
{
    public LlmApiKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidLlmApiKey,
                "OPENROUTER_API_KEY: укажите ключ OpenRouter без пробелов и управляющих символов.");
        }

        Value = value;
    }

    public string Value
    {
        get;
    }

    public override string ToString()
    {
        return "[скрыто]";
    }
}
