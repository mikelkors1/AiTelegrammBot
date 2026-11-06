using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Conversations;

public interface IConversationRepository
{
    Task<Result<ConversationSnapshot>> LoadAsync(long chatId, CancellationToken cancellationToken);
    Task<Result<ConversationState>> SetModeAsync(long chatId, AssistantMode mode, CancellationToken cancellationToken);
    Task<Result<ConversationState>> SetTemperatureAsync(long chatId, decimal temperature, CancellationToken cancellationToken);
    Task<Result<ConversationState>> ResetAsync(long chatId, CancellationToken cancellationToken);
    Task<Result<ConversationTurn>> BeginTurnAsync(long chatId, string userText, CancellationToken cancellationToken);
    Task<Result<ConversationTurnStatus>> CompleteTurnAsync(long chatId, Guid turnId, string assistantText, CancellationToken cancellationToken);
    Task<Result<ConversationTurnStatus>> FailTurnAsync(long chatId, Guid turnId, CancellationToken cancellationToken);
}
