using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Contracts;

public interface IConversationHistoryStore
{
    Task<IReadOnlyList<LlmMessage>> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ChatId chatId, CancellationToken cancellationToken);
    Task AppendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ConversationTurnId turnId, LlmMessage message, CancellationToken cancellationToken);
}
