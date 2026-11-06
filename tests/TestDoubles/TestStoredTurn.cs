using ItmoBot.Application.Conversations;

namespace ItmoBot.Tests.TestDoubles;

internal sealed record class TestStoredTurn(ConversationTurn Turn, string User, string? Assistant, ConversationTurnStatus Status);
