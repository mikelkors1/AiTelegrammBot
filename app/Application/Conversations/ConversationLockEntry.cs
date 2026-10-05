namespace ItmoBot.Application.Conversations;

internal sealed class ConversationLockEntry
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public int References
    {
        get; set;
    }
}
