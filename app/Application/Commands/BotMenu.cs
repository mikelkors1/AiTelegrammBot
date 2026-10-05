using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Commands;

public sealed class BotMenu : IBotMenu
{
    private readonly IReadOnlyList<BotMenuButton> buttons;
    private readonly TelegramReplyKeyboard mainKeyboard;
    private readonly TelegramReplyKeyboard settingsKeyboard;

    public BotMenu()
    {
        BotMenuButton[][] mainRows =
        [
            [Button("📚 Учёба", "/study"), Button("🌍 Перевод", "/translate"), Button("🧠 Опрос", "/quiz")],
            [Button("⚙️ Настройки", "/settings"), Button("🗑 Сброс истории", "/reset"), Button("👋 Помощь", "/start")],
        ];
        BotMenuButton[][] settingsRows =
        [
            [Button("Креативность 0.0", "/settings 0.0"), Button("Креативность 0.3", "/settings 0.3")],
            [Button("Креативность 0.7", "/settings 0.7"), Button("Креативность 1.0", "/settings 1.0")],
            [Button("⬅️ Главное меню", "/menu")],
        ];
        buttons = Array.AsReadOnly(mainRows.Concat(settingsRows).SelectMany(row => row).ToArray());
        mainKeyboard = CreateKeyboard(mainRows);
        settingsKeyboard = CreateKeyboard(settingsRows);
    }

    public TelegramReplyKeyboard GetKeyboard(BotMenuPage page)
    {
        return page switch
        {
            BotMenuPage.Main => mainKeyboard,
            BotMenuPage.Settings => settingsKeyboard,
            _ => throw new ArgumentOutOfRangeException(nameof(page)),
        };
    }

    public TelegramText? ResolveCommand(TelegramText text)
    {
        return buttons.FirstOrDefault(button => button.Label.Value == text.Value.Trim())?.Command;
    }

    private BotMenuButton Button(string label, string command)
    {
        return new BotMenuButton(new TelegramText(label), new TelegramText(command));
    }

    private TelegramReplyKeyboard CreateKeyboard(IEnumerable<IEnumerable<BotMenuButton>> rows)
    {
        return new TelegramReplyKeyboard(Array.AsReadOnly(rows.Select(row =>
            (IReadOnlyList<TelegramText>)Array.AsReadOnly(row.Select(button => button.Label).ToArray())).ToArray()));
    }
}
