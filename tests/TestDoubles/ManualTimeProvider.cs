using System.Threading.Channels;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ManualTimer> timers = [];
    private readonly Channel<TimeSpan> registrations = Channel.CreateUnbounded<TimeSpan>(new UnboundedChannelOptions
    {
        AllowSynchronousContinuations = false,
    });
    private long timestamp;

    public override long TimestampFrequency
    {
        get
        {
            return TimeSpan.TicksPerSecond;
        }
    }

    public override long GetTimestamp()
    {
        lock (gate)
        {
            return timestamp;
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        return DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        Schedule(timer, dueTime, period);
        return timer;
    }

    public bool Schedule(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        lock (gate)
        {
            if (timer.Disposed)
            {
                return false;
            }

            timer.Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : timestamp + dueTime.Ticks;
            timer.Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
            if (!timers.Contains(timer))
            {
                timers.Add(timer);
            }

            registrations.Writer.TryWrite(dueTime);
            return true;
        }
    }

    public void Remove(ManualTimer timer)
    {
        lock (gate)
        {
            timer.Disposed = true;
            timers.Remove(timer);
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        ManualTimer[] due;
        lock (gate)
        {
            timestamp += elapsed.Ticks;
            due = timers.Where(timer => timer.Due <= timestamp).ToArray();
            foreach (var timer in due)
            {
                timer.Due = timer.Period > 0 ? timestamp + timer.Period : long.MaxValue;
            }
        }

        foreach (var timer in due)
        {
            timer.Callback(timer.State);
        }
    }

    public async Task WaitForTimerAsync(TimeSpan delay)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (await registrations.Reader.ReadAsync(timeout.Token) != delay)
        {
            // Ignore registrations for the overall deadline or other waits.
        }
    }
}
