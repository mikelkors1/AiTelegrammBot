using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Configuration.Secrets;

public sealed record class TelegramBotToken
{
    public TelegramBotToken(string value)
    {
        if (string.IsNullOrEmpty(value) || !System.Text.RegularExpressions.Regex.IsMatch(value, @"^[0-9]+:[A-Za-z0-9_-]+$"))
        {
            throw new InputValidationException(ErrorCode.InvalidToken, "BOT_TOKEN: укажите токен, полученный у BotFather.");
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
