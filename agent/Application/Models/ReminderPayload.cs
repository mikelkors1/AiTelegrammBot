namespace ItmoBot.Application.Agent.Models;

public sealed record class ReminderPayload(string Text, ReminderSchedule Schedule);
