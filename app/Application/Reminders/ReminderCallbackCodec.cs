using ItmoBot.Shared.Validation;
using ItmoBot.Validation;

namespace ItmoBot.Application.Reminders;

public sealed class ReminderCallbackCodec
{
    public string Encode(ReminderButtonAction action)
    {
        return TelegramChecks.CallbackData($"r:{(action.Confirm ? "y" : "n")}:{action.ActionId:N}");
    }

    public ReminderButtonAction? Decode(string data)
    {
        var parts = data.Split(':');
        if (parts.Length != 3 || parts[0] != "r" || parts[1] is not ("y" or "n")
            || !Guid.TryParseExact(parts[2], "N", out var id) || id == Guid.Empty)
        {
            return null;
        }

        return new ReminderButtonAction(AgentChecks.PreparedActionId(id), parts[1] == "y");
    }
}
