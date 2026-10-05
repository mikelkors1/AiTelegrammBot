namespace ItmoBot.Application.Conversations;

internal sealed class ConversationLockLease(Action release) : IAsyncDisposable
{
    private Action? releaseAction = release;

    public ValueTask DisposeAsync()
    {
        var action = Interlocked.Exchange(ref releaseAction, null);
        if (action is not null)
        {
            action();
        }

        return ValueTask.CompletedTask;
    }
}
