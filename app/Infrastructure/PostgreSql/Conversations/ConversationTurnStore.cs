using ItmoBot.Application.Conversations;
using ItmoBot.Validation;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Conversations;

public sealed class ConversationTurnStore
{
    public async Task<Guid> BeginAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ConversationState state, CancellationToken cancellationToken)
    {
        Guid turnId = RequestChecks.ConversationTurnId(Guid.NewGuid());
        await using var command = new NpgsqlCommand("INSERT INTO conversation_turns(turn_id, chat_id, revision) VALUES (@turn, @chat, @revision)", connection, transaction);
        command.Parameters.AddWithValue("turn", turnId);
        command.Parameters.AddWithValue("chat", state.ChatId);
        command.Parameters.AddWithValue("revision", state.Revision);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return turnId;
    }

    public async Task<StoredConversationTurn?> ReadLockedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long chatId, Guid turnId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT revision, status FROM conversation_turns WHERE chat_id = @chat AND turn_id = @turn FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("chat", chatId);
        command.Parameters.AddWithValue("turn", turnId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var status = reader.GetString(1) switch
        {
            "pending" => ConversationTurnStatus.Pending,
            "succeeded" => ConversationTurnStatus.Succeeded,
            "failed" => ConversationTurnStatus.Failed,
            _ => throw new InvalidOperationException("В БД сохранён неизвестный статус хода диалога."),
        };
        return new StoredConversationTurn(RequestChecks.ConversationRevision(reader.GetInt64(0)), status);
    }

    public async Task SetStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid turnId, ConversationTurnStatus status, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE conversation_turns SET status = @status WHERE turn_id = @turn", connection, transaction);
        command.Parameters.AddWithValue("status", status.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("turn", turnId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
