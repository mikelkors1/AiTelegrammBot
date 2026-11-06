using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Shared.Validation;

namespace ItmoBot.Application.Agent;

public sealed class ReminderPayloadHasher
{
    public string Hash(ReminderPayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            payload.Text,
            payload.Schedule.Moment.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            payload.Schedule.TimeZone.Value.Id,
        });
        return AgentChecks.ArgumentsHash(Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
