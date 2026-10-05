using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed record class ConversationState(ChatId ChatId, AssistantMode Mode, Temperature Temperature, ConversationRevision Revision);
