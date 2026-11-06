using System.Security.Cryptography;

namespace ItmoBot.Agent.Security;

public sealed class ActionContextVerifier(McpSigningKey? key, ActionContextCodec codec, TimeProvider clock)
{
    public bool Verify(TrustedActionContext? context)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (key is null || context is null || context.Version != 1 || context.ExpiresAt <= now || context.ExpiresAt > now + 60
            || context.Signature.Length > 64)
        {
            return false;
        }

        Span<byte> signature = stackalloc byte[32];
        if (!Convert.TryFromBase64String(context.Signature, signature, out var length) || length != 32)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(signature, key.ComputeSignature(codec.Encode(context)));
    }
}
