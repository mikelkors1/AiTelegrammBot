using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class CodeLanguage
{
    public CodeLanguage(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 40
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '+' or '#')))
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidCodeLanguage, "Некорректное обозначение языка кода.");
        }

        Value = value.ToLowerInvariant() switch
        {
            "c#" or "cs" => "csharp",
            "f#" => "fsharp",
            "js" => "javascript",
            "ts" => "typescript",
            "py" => "python",
            _ => value.ToLowerInvariant(),
        };
    }

    public string Value
    {
        get;
    }
}
