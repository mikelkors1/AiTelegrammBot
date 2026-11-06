namespace ItmoBot.Application.Agent.Models;

public sealed record class AuditArgumentsSummary(int? ReminderTextLength = null, DateTimeOffset? RemindAt = null, UserTimeZone? TimeZone = null, string? City = null, DateOnly? ScheduleDate = null);
