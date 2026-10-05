using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Infrastructure.PostgreSql.Contracts;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Conversations;

public sealed class ConversationHistoryStore : IConversationHistoryStore
{
    public async Task<IReadOnlyList<LlmMessage>> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ChatId chatId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT m.role, m.content
            FROM conversation_turns t JOIN conversation_messages m ON m.turn_id = t.turn_id
            WHERE t.chat_id = @chat AND t.status = 'succeeded'
            ORDER BY t.sequence, CASE m.role WHEN 'user' THEN 0 ELSE 1 END
            """, connection, transaction);
        command.Parameters.AddWithValue("chat", chatId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var history = new List<LlmMessage>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var role = reader.GetString(0) switch
            {
                "user" => LlmMessageRole.User,
                "assistant" => LlmMessageRole.Assistant,
                _ => throw new InvalidOperationException("История содержит недопустимую роль."),
            };
            history.Add(new LlmMessage(role, new LlmText(reader.GetString(1))));
        }

        return history.AsReadOnly();
    }

    public async Task AppendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ConversationTurnId turnId, LlmMessage message, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("INSERT INTO conversation_messages(turn_id, role, content) VALUES (@turn, @role, @content)", connection, transaction);
        command.Parameters.AddWithValue("turn", turnId.Value);
        command.Parameters.AddWithValue("role", message.Role.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("content", message.Content.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
