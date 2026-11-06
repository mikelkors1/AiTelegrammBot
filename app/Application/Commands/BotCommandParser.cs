using System.Globalization;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Conversations;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Validation;

namespace ItmoBot.Application.Commands;

public sealed class BotCommandParser(BotMenu menu)
{
    public BotCommandParseResult Parse(string text)
    {
        var input = (menu.ResolveCommand(text) ?? text).Trim();
        if (!input.StartsWith('/'))
        {
            return new BotCommandParseResult.NotCommand();
        }

        var parts = input.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0].ToLowerInvariant();
        var argument = parts.Length == 2 ? parts[1].Trim() : "";
        if (name == "/settings")
        {
            return ParseSettings(argument);
        }

        if (name == "/timezone")
        {
            try
            {
                return new BotCommandParseResult.Success(argument.Length == 0 ? new BotCommand.ShowTimeZone() : new BotCommand.SetTimeZone(new UserTimeZone(argument)));
            }
            catch (ArgumentException)
            {
                return new BotCommandParseResult.Failure(new AppError(ErrorCode.InvalidTimeZone, "Укажите существующий часовой пояс IANA, например Europe/Moscow."));
            }
        }

        BotCommand? command = name switch
        {
            "/start" => new BotCommand.Start(),
            "/menu" => new BotCommand.ShowMainMenu(),
            "/study" => new BotCommand.ChangeMode(AssistantMode.Study),
            "/translate" => new BotCommand.ChangeMode(AssistantMode.Translate),
            "/quiz" => new BotCommand.ChangeMode(AssistantMode.Quiz),
            "/tools" => new BotCommand.ShowTools(),
            "/why" => new BotCommand.ShowWhy(),
            "/reset" => new BotCommand.Reset(),
            _ => null,
        };
        if (command is null)
        {
            return new BotCommandParseResult.Failure(new AppError(ErrorCode.UnknownBotCommand, "Неизвестная команда."));
        }

        if (argument.Length > 0)
        {
            return new BotCommandParseResult.Failure(new AppError(ErrorCode.InvalidBotCommandArgument, "Эта команда не принимает аргументы."));
        }

        return new BotCommandParseResult.Success(command);
    }

    private BotCommandParseResult ParseSettings(string argument)
    {
        if (argument.Length == 0)
        {
            return new BotCommandParseResult.Success(new BotCommand.ShowSettings());
        }

        if (!decimal.TryParse(argument, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return InvalidTemperature();
        }

        try
        {
            return new BotCommandParseResult.Success(new BotCommand.ChangeCreativity(RequestChecks.Temperature(value)));
        }
        catch (InputValidationException)
        {
            return InvalidTemperature();
        }
    }

    private BotCommandParseResult InvalidTemperature()
    {
        return new BotCommandParseResult.Failure(new AppError(ErrorCode.InvalidTemperature,
            "Используйте /settings 0.0, /settings 0.3, /settings 0.7 или /settings 1.0."));
    }
}
