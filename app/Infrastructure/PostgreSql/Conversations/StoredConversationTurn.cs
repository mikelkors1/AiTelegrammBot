using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Infrastructure.PostgreSql.Conversations;

public sealed record class StoredConversationTurn(ConversationRevision Revision, ConversationTurnStatus Status);
