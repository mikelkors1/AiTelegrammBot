using ItmoBot.Application.Agent.Models;
using ItmoBot.Mcp.Schedule.Contracts;
using ItmoBot.Mcp.Schedule.Models;
using ItmoBot.Mcp.Schedule.ResultTypes;
using ItmoBot.Mcp.Validation;
using NodaTime;

namespace ItmoBot.Mcp.Schedule;

public sealed class ScheduleService(IScheduleSource source, UserTimeZone timezone, TimeProvider clock)
{
    public async Task<ScheduleResult<ScheduleDay>> GetDayAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var loaded = await source.LoadAsync(cancellationToken);
        if (loaded is ScheduleResult<ScheduleSnapshot>.Failure failure)
        {
            return new ScheduleResult<ScheduleDay>.Failure(failure.Error);
        }

        return new ScheduleResult<ScheduleDay>.Success(((ScheduleResult<ScheduleSnapshot>.Success)loaded).Value.GetDay(date));
    }

    public async Task<ScheduleResult<ScheduleWeek>> GetCurrentWeekAsync(CancellationToken cancellationToken)
    {
        var today = Instant.FromDateTimeOffset(clock.GetUtcNow()).InZone(timezone.Value).Date;
        var monday = today.PlusDays(1 - (int)today.DayOfWeek);
        var start = new DateOnly(monday.Year, monday.Month, monday.Day);
        var loaded = await source.LoadAsync(cancellationToken);
        if (loaded is ScheduleResult<ScheduleSnapshot>.Failure failure)
        {
            return new ScheduleResult<ScheduleWeek>.Failure(failure.Error);
        }

        var snapshot = ((ScheduleResult<ScheduleSnapshot>.Success)loaded).Value;
        var days = Enumerable.Range(0, 7).Select(offset => snapshot.GetDay(ScheduleChecks.ScheduleDate(start.AddDays(offset)))).ToArray();
        return new ScheduleResult<ScheduleWeek>.Success(new ScheduleWeek(ScheduleChecks.ScheduleDate(start),
            ScheduleChecks.ScheduleDate(start.AddDays(6)), Array.AsReadOnly(days)));
    }
}
