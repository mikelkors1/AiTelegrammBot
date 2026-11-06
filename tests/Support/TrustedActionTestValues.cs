using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent;
using ItmoBot.Shared.Validation;

namespace ItmoBot.Tests.Support;

internal sealed class TrustedActionTestValues
{
    public PreparedAction Action(TimeProvider clock)
    {
        var zone = new UserTimeZone("Europe/Moscow");
        var payload = new ReminderPayload(AgentChecks.ReminderText("Проверить расписание"),
            new ReminderSchedule(zone.ConvertInstant(clock.GetUtcNow().AddDays(1)), zone));
        return new PreparedAction(AgentChecks.PreparedActionId(Guid.NewGuid()), new ActionOwner(AgentChecks.OwnerId(42), AgentChecks.ChatId(42)),
            AgentChecks.UpdateId(100), payload, new ReminderPayloadHasher().Hash(payload), PreparedActionStatus.Confirmed,
            clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(5));
    }
}
