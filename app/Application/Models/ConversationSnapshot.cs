using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed record class ConversationSnapshot(ConversationState State, IReadOnlyList<LlmMessage> History);
