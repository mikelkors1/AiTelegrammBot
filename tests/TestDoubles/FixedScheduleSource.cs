using ItmoBot.Mcp.Schedule.Contracts;
using ItmoBot.Mcp.Schedule.Models;
using ItmoBot.Mcp.Schedule.ResultTypes;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FixedScheduleSource(ScheduleResult<ScheduleSnapshot> result) : IScheduleSource
{
    public int LoadCalls
    {
        get; private set;
    }

    public Task<ScheduleResult<ScheduleSnapshot>> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadCalls++;
        return Task.FromResult(result);
    }
}
