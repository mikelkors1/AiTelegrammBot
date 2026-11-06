using ItmoBot.Application.Agent.Models;

namespace ItmoBot.Agent.Security;

public sealed record class TrustedActionContext(int Version, Guid ActionId,
    ActionOwner Owner, string ArgumentsHash, long ExpiresAt, string Signature);
