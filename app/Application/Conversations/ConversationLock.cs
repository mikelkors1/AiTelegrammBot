using ItmoBot.Application.Contracts;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Conversations;

public sealed class ConversationLock : IConversationLock
{
    private readonly object gate = new();
    private readonly Dictionary<ChatId, ConversationLockEntry> entries = [];

    public async Task<IAsyncDisposable> AcquireAsync(ChatId chatId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConversationLockEntry entry;
        lock (gate)
        {
            if (!entries.TryGetValue(chatId, out entry!))
            {
                entry = new ConversationLockEntry();
                entries.Add(chatId, entry);
            }

            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new ConversationLockLease(() => Release(chatId, entry, acquired: true));
        }
        catch
        {
            Release(chatId, entry, acquired: false);
            throw;
        }
    }

    private void Release(ChatId chatId, ConversationLockEntry entry, bool acquired)
    {
        lock (gate)
        {
            if (acquired)
            {
                entry.Semaphore.Release();
            }

            entry.References--;
            if (entry.References == 0)
            {
                entries.Remove(chatId);
                entry.Semaphore.Dispose();
            }
        }
    }
}
