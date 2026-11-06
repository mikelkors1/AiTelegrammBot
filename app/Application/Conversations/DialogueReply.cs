using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Conversations.Context;
using ItmoBot.Application.Llm;

namespace ItmoBot.Application.Conversations;

public sealed record class DialogueReply(LlmResponse Response, PreparedLlmRequest Prepared, PreparedAction? PendingAction = null);
