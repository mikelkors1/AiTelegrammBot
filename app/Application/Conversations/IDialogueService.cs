using ItmoBot.Application.AgentLoop;
using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Conversations;

public interface IDialogueService
{
    Task<Result<DialogueReply>> RespondAsync(long chatId, string text, CancellationToken cancellationToken, AgentTurnContext? context = null);
}
