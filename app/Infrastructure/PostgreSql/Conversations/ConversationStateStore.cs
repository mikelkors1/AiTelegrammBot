using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Infrastructure.PostgreSql.Contracts;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Conversations;

public sealed class ConversationStateStore : IConversationStateStore
{
    public async Task<ConversationState> LoadLockedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ChatId chatId, CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand("INSERT INTO conversation_users(chat_id) VALUES (@chat) ON CONFLICT DO NOTHING", connection, transaction);
        insert.Parameters.AddWithValue("chat", chatId.Value);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await using var select = new NpgsqlCommand("SELECT mode, temperature, revision FROM conversation_users WHERE chat_id = @chat FOR UPDATE", connection, transaction);
        select.Parameters.AddWithValue("chat", chatId.Value);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Не удалось прочитать состояние диалога.");
        }

        var mode = reader.GetString(0) switch
        {
            "study" => AssistantMode.Study,
            "translate" => AssistantMode.Translate,
            "quiz" => AssistantMode.Quiz,
            _ => throw new InvalidOperationException("В БД сохранён неизвестный режим."),
        };
        return new ConversationState(chatId, mode, new Temperature(reader.GetDecimal(1)), new ConversationRevision(reader.GetInt64(2)));
    }

    public async Task<ConversationState> SetTemperatureAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ConversationState state, Temperature temperature, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE conversation_users SET temperature = @temperature WHERE chat_id = @chat", connection, transaction);
        command.Parameters.AddWithValue("temperature", temperature.Value);
        command.Parameters.AddWithValue("chat", state.ChatId.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return state with
        {
            Temperature = temperature
        };
    }

    public async Task<ConversationState> ClearAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ConversationState state, AssistantMode mode, CancellationToken cancellationToken)
    {
        await using var delete = new NpgsqlCommand("DELETE FROM conversation_turns WHERE chat_id = @chat", connection, transaction);
        delete.Parameters.AddWithValue("chat", state.ChatId.Value);
        await delete.ExecuteNonQueryAsync(cancellationToken);
        await using var update = new NpgsqlCommand("UPDATE conversation_users SET mode = @mode, revision = revision + 1 WHERE chat_id = @chat RETURNING revision", connection, transaction);
        update.Parameters.AddWithValue("mode", mode.ToString().ToLowerInvariant());
        update.Parameters.AddWithValue("chat", state.ChatId.Value);
        var revision = (long)(await update.ExecuteScalarAsync(cancellationToken))!;
        return state with
        {
            Mode = mode,
            Revision = new ConversationRevision(revision)
        };
    }
}
