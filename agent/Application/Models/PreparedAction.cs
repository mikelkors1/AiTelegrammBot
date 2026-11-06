namespace ItmoBot.Application.Agent.Models;

public sealed record class PreparedAction(Guid Id, ActionOwner Owner, int SourceUpdate, ReminderPayload Payload, string ArgumentsHash, PreparedActionStatus Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
