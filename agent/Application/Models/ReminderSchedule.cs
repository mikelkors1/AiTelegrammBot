namespace ItmoBot.Application.Agent.Models;

public sealed record class ReminderSchedule
{
    public ReminderSchedule(DateTimeOffset moment, UserTimeZone timezone)
    {
        ArgumentNullException.ThrowIfNull(timezone);
        if (timezone.ConvertInstant(moment).Offset != moment.Offset)
        {
            throw new ArgumentException("Смещение времени не соответствует часовому поясу в этот момент.");
        }

        // PostgreSQL хранит timestamptz с точностью до микросекунды.
        Moment = moment.AddTicks(-(moment.Ticks % TimeSpan.TicksPerMicrosecond));
        TimeZone = timezone;
    }

    public DateTimeOffset Moment
    {
        get;
    }

    public UserTimeZone TimeZone
    {
        get;
    }
}
