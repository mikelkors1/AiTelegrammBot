using System.Text.Json.Nodes;
using System.Text.Json;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Shared.Validation;

namespace ItmoBot.Agent.Security;

public sealed class ActionContextMetadata
{
    public JsonObject Encode(TrustedActionContext context)
    {
        return new JsonObject
        {
            ["itmobot/action"] = new JsonObject
            {
                ["version"] = context.Version,
                ["action_id"] = context.ActionId.ToString("D"),
                ["owner_id"] = context.Owner.OwnerId,
                ["chat_id"] = context.Owner.ChatId,
                ["arguments_hash"] = context.ArgumentsHash,
                ["expires_at"] = context.ExpiresAt,
                ["signature"] = context.Signature,
            },
        };
    }

    public TrustedActionContext? Decode(JsonObject? metadata)
    {
        if (metadata?["itmobot/action"] is not JsonObject value || value.Count != 7 || value.ToJsonString().Length > 2048)
        {
            return null;
        }

        try
        {
            return new TrustedActionContext(value["version"]!.GetValue<int>(),
                AgentChecks.PreparedActionId(Guid.ParseExact(value["action_id"]!.GetValue<string>(), "D")),
                new ActionOwner(AgentChecks.OwnerId(value["owner_id"]!.GetValue<long>()), AgentChecks.ChatId(value["chat_id"]!.GetValue<long>())),
                AgentChecks.ArgumentsHash(value["arguments_hash"]!.GetValue<string>()),
                value["expires_at"]!.GetValue<long>(), value["signature"]!.GetValue<string>());
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException or NullReferenceException or JsonException)
        {
            return null;
        }
    }
}
