using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Conversations.Context;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Results;
using ItmoBot.Validation;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class PendingDialogueService : IDialogueService
{
    public PreparedAction? Action
    {
        get; set;
    }
    public int Calls
    {
        get; private set;
    }

    public Task<Result<DialogueReply>> RespondAsync(long chatId, string text, CancellationToken cancellationToken, AgentTurnContext? context = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        var request = new PreparedLlmRequest(new LlmRequest([new LlmMessage(LlmMessageRole.User, text)], RequestChecks.Temperature(0.0m)),
            RequestChecks.PromptVersion("test"), 1, 0, 0);
        return Task.FromResult<Result<DialogueReply>>(new Result<DialogueReply>.Success(new DialogueReply(
            new LlmResponse(RequestChecks.LlmText("Непроверенный текст модели"), RequestChecks.ModelName("test/model"), null, null), request, Action)));
    }
}
