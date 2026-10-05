using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed record class ConversationTurn(ConversationTurnId Id, ConversationState State);
