using ItmoBot.Application.Conversations;

namespace ItmoBot.Infrastructure.PostgreSql.Conversations;

public sealed record class StoredConversationTurn(long Revision, ConversationTurnStatus Status);
