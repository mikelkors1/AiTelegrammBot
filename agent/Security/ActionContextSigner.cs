using ItmoBot.Application.Agent.Models;

namespace ItmoBot.Agent.Security;

public sealed class ActionContextSigner(McpSigningKey key, ActionContextCodec codec, TimeProvider clock)
{
    public TrustedActionContext Sign(PreparedAction action)
    {
        var context = new TrustedActionContext(1, action.Id, action.Owner, action.ArgumentsHash,
            clock.GetUtcNow().ToUnixTimeSeconds() + 60, "");
        return context with
        {
            Signature = Convert.ToBase64String(key.ComputeSignature(codec.Encode(context))),
        };
    }
}
