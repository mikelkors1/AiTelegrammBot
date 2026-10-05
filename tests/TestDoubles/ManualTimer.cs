namespace ItmoBot.Tests.TestDoubles;

internal sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
{
    public TimerCallback Callback { get; } = callback;
    public object? State { get; } = state;
    public long Due
    {
        get; set;
    }
    public long Period
    {
        get; set;
    }
    public bool Disposed
    {
        get; set;
    }

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        return owner.Schedule(this, dueTime, period);
    }

    public void Dispose()
    {
        owner.Remove(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
