using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Commands;

public sealed class BotCommandHandler(IConversationRepository repository, ICommandReplyFormatter formatter) : IBotCommandHandler
{
    public async Task<BotCommandExecutionResult> ExecuteAsync(ChatId chatId, BotCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (command)
        {
            case BotCommand.Start:
            case BotCommand.ShowMainMenu:
                return await ReadAsync(chatId, formatter.Welcome, cancellationToken);
            case BotCommand.ShowSettings:
                return await ReadAsync(chatId, formatter.Settings, cancellationToken);
            case BotCommand.ChangeCreativity change:
                return Format(await repository.SetTemperatureAsync(chatId, change.Temperature, cancellationToken), formatter.Settings);
            case BotCommand.ChangeMode change:
                return Format(await repository.SetModeAsync(chatId, change.Mode, cancellationToken), formatter.ModeChanged);
            case BotCommand.Reset:
                return Format(await repository.ResetAsync(chatId, cancellationToken), formatter.Reset);
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private async Task<BotCommandExecutionResult> ReadAsync(ChatId chatId, Func<ConversationState, LlmText> format, CancellationToken cancellationToken)
    {
        var loaded = await repository.LoadAsync(chatId, cancellationToken);
        if (loaded is ConversationResult<ConversationSnapshot>.Failure failure)
        {
            return new BotCommandExecutionResult.Failure(failure.Error);
        }

        return new BotCommandExecutionResult.Success(format(((ConversationResult<ConversationSnapshot>.Success)loaded).Value.State));
    }

    private BotCommandExecutionResult Format(ConversationResult<ConversationState> result, Func<ConversationState, LlmText> format)
    {
        if (result is ConversationResult<ConversationState>.Failure failure)
        {
            return new BotCommandExecutionResult.Failure(failure.Error);
        }

        return new BotCommandExecutionResult.Success(format(((ConversationResult<ConversationState>.Success)result).Value));
    }
}
