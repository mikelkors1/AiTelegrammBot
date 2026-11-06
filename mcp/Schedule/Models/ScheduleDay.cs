namespace ItmoBot.Mcp.Schedule.Models;

public sealed record class ScheduleDay(DateOnly Date, IReadOnlyList<Lesson> Lessons);
