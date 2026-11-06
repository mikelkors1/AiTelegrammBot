using NodaTime;

namespace ItmoBot.Application.Agent.Models;

public sealed record class UserTimeZone
{
    public UserTimeZone(string value)
    {
        Value = DateTimeZoneProviders.Tzdb.GetZoneOrNull(value)
            ?? throw new ArgumentException("Укажите существующий часовой пояс IANA.");
    }

    public DateTimeZone Value
    {
        get;
    }

    public DateTimeOffset ConvertInstant(DateTimeOffset instant)
    {
        return Instant.FromDateTimeOffset(instant).InZone(Value).ToDateTimeOffset();
    }
}
