using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IBotCommandHandler
{
    Task<BotCommandExecutionResult> ExecuteAsync(ChatId chatId, BotCommand command, CancellationToken cancellationToken);
}
