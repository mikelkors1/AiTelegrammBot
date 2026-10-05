using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public abstract record class BotCommand
{
    private BotCommand()
    {
    }

    public sealed record class Start : BotCommand;
    public sealed record class ShowMainMenu : BotCommand;
    public sealed record class ChangeMode(AssistantMode Mode) : BotCommand;
    public sealed record class ShowSettings : BotCommand;
    public sealed record class ChangeCreativity(Temperature Temperature) : BotCommand;
    public sealed record class Reset : BotCommand;
}
