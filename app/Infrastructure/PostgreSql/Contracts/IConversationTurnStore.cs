using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Infrastructure.PostgreSql.Conversations;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Contracts;

public interface IConversationTurnStore
{
    Task<ConversationTurnId> BeginAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ConversationState state, CancellationToken cancellationToken);
    Task<StoredConversationTurn?> ReadLockedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ChatId chatId, ConversationTurnId turnId, CancellationToken cancellationToken);
    Task SetStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ConversationTurnId turnId, ConversationTurnStatus status, CancellationToken cancellationToken);
}
