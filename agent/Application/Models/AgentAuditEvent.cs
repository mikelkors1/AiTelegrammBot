namespace ItmoBot.Application.Agent.Models;

public sealed record class AgentAuditEvent(Guid RequestId, long Owner, long ChatId, int UpdateId, DateTimeOffset OccurredAt, AuditAction Action, AuditStatus Status, string Reason, AuditArgumentsSummary Arguments, string? Tool = null, TimeSpan? Duration = null);
