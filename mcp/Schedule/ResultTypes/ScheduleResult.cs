using ItmoBot.Mcp.Schedule.Models;

namespace ItmoBot.Mcp.Schedule.ResultTypes;

public abstract record class ScheduleResult<T>
{
    private ScheduleResult()
    {
    }

    public sealed record class Success(T Value) : ScheduleResult<T>;

    public sealed record class Failure(ScheduleError Error) : ScheduleResult<T>;
}
