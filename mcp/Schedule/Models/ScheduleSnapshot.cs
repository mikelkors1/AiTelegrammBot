using System.Collections.ObjectModel;

namespace ItmoBot.Mcp.Schedule.Models;

public sealed class ScheduleSnapshot
{
    private readonly IReadOnlyDictionary<DateOnly, IReadOnlyList<Lesson>> days;

    public ScheduleSnapshot(IReadOnlyDictionary<DateOnly, IReadOnlyList<Lesson>> days)
    {
        this.days = new ReadOnlyDictionary<DateOnly, IReadOnlyList<Lesson>>(days.ToDictionary(
            entry => entry.Key, entry => (IReadOnlyList<Lesson>)Array.AsReadOnly(entry.Value.ToArray())));
    }

    public ScheduleDay GetDay(DateOnly date)
    {
        return new ScheduleDay(date, days.TryGetValue(date, out var lessons) ? lessons : []);
    }
}
