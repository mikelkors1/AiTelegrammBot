using ItmoBot.Application.Agent.Models;

namespace ItmoBot.Application.AgentLoop;

public sealed record class AgentTurnContext(long Owner, long ChatId, int UpdateId, DateTimeOffset? StartedAt = null, string? DecisionReason = null, UserTimeZone? TimeZone = null);
