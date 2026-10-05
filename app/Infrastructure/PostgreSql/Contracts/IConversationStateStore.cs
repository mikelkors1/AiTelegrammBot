using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql.Contracts;

public interface IConversationStateStore
{
    Task<ConversationState> LoadLockedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ChatId chatId, CancellationToken cancellationToken);
    Task<ConversationState> SetTemperatureAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ConversationState state, Temperature temperature, CancellationToken cancellationToken);
    Task<ConversationState> ClearAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ConversationState state, AssistantMode mode, CancellationToken cancellationToken);
}
