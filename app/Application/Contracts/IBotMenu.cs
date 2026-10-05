using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IBotMenu
{
    TelegramReplyKeyboard GetKeyboard(BotMenuPage page);

    TelegramText? ResolveCommand(TelegramText text);
}
