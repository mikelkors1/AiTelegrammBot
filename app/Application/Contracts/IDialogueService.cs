using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IDialogueService
{
    Task<DialogueResult> RespondAsync(ChatId chatId, LlmText text, CancellationToken cancellationToken);
}
