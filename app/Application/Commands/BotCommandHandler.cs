using ItmoBot.Application.Conversations;
using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Commands;

public sealed class BotCommandHandler(IConversationRepository repository, CommandReplyFormatter formatter)
{
    public async Task<Result<string>> ExecuteAsync(long chatId, BotCommand command, CancellationToken cancellationToken)
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

    private async Task<Result<string>> ReadAsync(long chatId, Func<ConversationState, string> format, CancellationToken cancellationToken)
    {
        var loaded = await repository.LoadAsync(chatId, cancellationToken);
        if (loaded is Result<ConversationSnapshot>.Failure failure)
        {
            return new Result<string>.Failure(failure.Error);
        }

        return new Result<string>.Success(format(((Result<ConversationSnapshot>.Success)loaded).Value.State));
    }

    private Result<string> Format(Result<ConversationState> result, Func<ConversationState, string> format)
    {
        if (result is Result<ConversationState>.Failure failure)
        {
            return new Result<string>.Failure(failure.Error);
        }

        return new Result<string>.Success(format(((Result<ConversationState>.Success)result).Value));
    }
}
