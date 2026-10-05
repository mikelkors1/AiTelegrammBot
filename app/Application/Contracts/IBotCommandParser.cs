using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IBotCommandParser
{
    BotCommandParseResult Parse(TelegramText text);
}
