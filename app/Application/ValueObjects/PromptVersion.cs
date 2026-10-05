using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class PromptVersion
{
    public PromptVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidPrompt, "Версия инструкции должна содержать латинские буквы, цифры и дефисы.");
        }

        Value = value;
    }

    public string Value
    {
        get;
    }
}
