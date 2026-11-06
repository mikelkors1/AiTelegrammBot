namespace ItmoBot.Application.Agent.Models;

public sealed record class StoredReminder(Guid Id, Guid ActionId, ActionOwner Owner, ReminderPayload Payload);
