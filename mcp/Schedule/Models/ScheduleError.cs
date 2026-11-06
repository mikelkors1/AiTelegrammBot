namespace ItmoBot.Mcp.Schedule.Models;

public sealed record class ScheduleError(ScheduleErrorCode Code, string Message, bool Retryable = false);
