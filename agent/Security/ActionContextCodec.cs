using System.Globalization;
using System.Text.Json;

namespace ItmoBot.Agent.Security;

public sealed class ActionContextCodec
{
    public byte[] Encode(TrustedActionContext context)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            context.Version.ToString(CultureInfo.InvariantCulture),
            context.ActionId.ToString("D"),
            context.Owner.OwnerId.ToString(CultureInfo.InvariantCulture),
            context.Owner.ChatId.ToString(CultureInfo.InvariantCulture),
            context.ArgumentsHash,
            context.ExpiresAt.ToString(CultureInfo.InvariantCulture),
        });
    }
}
