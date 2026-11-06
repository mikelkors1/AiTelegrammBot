using ItmoBot.Application.Llm;

namespace ItmoBot.Application.Conversations;

public sealed record class ConversationSnapshot(ConversationState State, IReadOnlyList<LlmMessage> History);
