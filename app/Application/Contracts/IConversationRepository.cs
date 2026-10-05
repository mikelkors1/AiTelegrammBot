using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IConversationRepository
{
    Task<ConversationResult<ConversationSnapshot>> LoadAsync(ChatId chatId, CancellationToken cancellationToken);
    Task<ConversationResult<ConversationState>> SetModeAsync(ChatId chatId, AssistantMode mode, CancellationToken cancellationToken);
    Task<ConversationResult<ConversationState>> SetTemperatureAsync(ChatId chatId, Temperature temperature, CancellationToken cancellationToken);
    Task<ConversationResult<ConversationState>> ResetAsync(ChatId chatId, CancellationToken cancellationToken);
    Task<ConversationResult<ConversationTurn>> BeginTurnAsync(ChatId chatId, LlmText userText, CancellationToken cancellationToken);
    Task<ConversationResult<ConversationTurnStatus>> CompleteTurnAsync(ChatId chatId, ConversationTurnId turnId, LlmText assistantText, CancellationToken cancellationToken);
    Task<ConversationResult<ConversationTurnStatus>> FailTurnAsync(ChatId chatId, ConversationTurnId turnId, CancellationToken cancellationToken);
}
