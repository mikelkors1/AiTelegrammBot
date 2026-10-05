using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Infrastructure.PostgreSql.Contracts;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Conversations;

public sealed class ConversationTurnStore : IConversationTurnStore
{
    public async Task<ConversationTurnId> BeginAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ConversationState state, CancellationToken cancellationToken)
    {
        var turnId = new ConversationTurnId(Guid.NewGuid());
        await using var command = new NpgsqlCommand("INSERT INTO conversation_turns(turn_id, chat_id, revision) VALUES (@turn, @chat, @revision)", connection, transaction);
        command.Parameters.AddWithValue("turn", turnId.Value);
        command.Parameters.AddWithValue("chat", state.ChatId.Value);
        command.Parameters.AddWithValue("revision", state.Revision.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return turnId;
    }

    public async Task<StoredConversationTurn?> ReadLockedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ChatId chatId, ConversationTurnId turnId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT revision, status FROM conversation_turns WHERE chat_id = @chat AND turn_id = @turn FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("chat", chatId.Value);
        command.Parameters.AddWithValue("turn", turnId.Value);
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
        return new StoredConversationTurn(new ConversationRevision(reader.GetInt64(0)), status);
    }

    public async Task SetStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ConversationTurnId turnId, ConversationTurnStatus status, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE conversation_turns SET status = @status WHERE turn_id = @turn", connection, transaction);
        command.Parameters.AddWithValue("status", status.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("turn", turnId.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
