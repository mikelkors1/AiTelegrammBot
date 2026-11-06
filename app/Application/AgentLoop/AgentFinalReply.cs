namespace ItmoBot.Application.AgentLoop;

public sealed record class AgentFinalReply(string Text, string Reason, bool RequiresClarification);
