using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Tests.TestDoubles;

internal sealed record class TestStoredTurn(ConversationTurn Turn, LlmText User, LlmText? Assistant, ConversationTurnStatus Status);
