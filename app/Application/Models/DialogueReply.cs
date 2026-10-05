namespace ItmoBot.Application.Models;

public sealed record class DialogueReply(LlmResponse Response, PreparedLlmRequest Prepared);
