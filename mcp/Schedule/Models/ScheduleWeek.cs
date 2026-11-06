namespace ItmoBot.Mcp.Schedule.Models;

public sealed record class ScheduleWeek(DateOnly Start, DateOnly End, IReadOnlyList<ScheduleDay> Days);
