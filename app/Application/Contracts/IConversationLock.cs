using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IConversationLock
{
    Task<IAsyncDisposable> AcquireAsync(ChatId chatId, CancellationToken cancellationToken);
}
