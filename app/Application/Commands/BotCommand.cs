using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Conversations;

namespace ItmoBot.Application.Commands;

public abstract record class BotCommand
{
    private BotCommand()
    {
    }

    public sealed record class Start : BotCommand;
    public sealed record class ShowMainMenu : BotCommand;
    public sealed record class ChangeMode(AssistantMode Mode) : BotCommand;
    public sealed record class ShowSettings : BotCommand;
    public sealed record class ChangeCreativity(decimal Temperature) : BotCommand;
    public sealed record class ShowTools : BotCommand;
    public sealed record class ShowTimeZone : BotCommand;
    public sealed record class SetTimeZone(UserTimeZone TimeZone) : BotCommand;
    public sealed record class ShowWhy : BotCommand;
    public sealed record class Reset : BotCommand;
}
