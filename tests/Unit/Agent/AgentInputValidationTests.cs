using System.Globalization;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Agent;

public sealed class AgentInputValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateOwner_NonPositiveId_ThrowsArgumentException(long value)
    {
        // Arrange
        var owner = value;

        // Act
        var create = () => AgentChecks.OwnerId(owner);

        // Assert
        Assert.Throws<ArgumentException>(() => create());
    }

    [Theory]
    [InlineData(84)]
    [InlineData(-42)]
    public void Validate_ActionOwner_RequiresPrivateChat(long chat)
    {
        // Arrange
        long owner = AgentChecks.OwnerId(42);

        // Act
        var create = () => new ActionOwner(owner, AgentChecks.ChatId(chat));

        // Assert
        Assert.Throws<ArgumentException>(() => create());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyReminderText_IsRejected(string text)
    {
        // Arrange
        var value = text;

        // Act
        var create = () => AgentChecks.ReminderText(value);

        // Assert
        Assert.Throws<ArgumentException>(() => create());
    }

    [Fact]
    public void Validate_Schedule_RejectsOffsetThatDoesNotMatchTimezoneAtInstant()
    {
        // Arrange
        var zone = new UserTimeZone("Europe/Berlin");
        var incorrect = new DateTimeOffset(2030, 7, 1, 12, 0, 0, TimeSpan.FromHours(1));

        // Act
        var create = () => new ReminderSchedule(incorrect, zone);

        // Assert
        Assert.Throws<ArgumentException>(() => create());
    }

    [Fact]
    public void Validate_Schedule_PreservesBothInstantsOfRepeatedHourAndNormalizesDatabasePrecision()
    {
        // Arrange
        var zone = new UserTimeZone("Europe/Berlin");
        var first = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2)).AddTicks(17);
        var second = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1));

        // Act
        var earlier = new ReminderSchedule(first, zone);
        var later = new ReminderSchedule(second, zone);

        // Assert
        Assert.Equal(0, earlier.Moment.Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(TimeSpan.FromHours(2), earlier.Moment.Offset);
        Assert.Equal(TimeSpan.FromHours(1), later.Moment.Offset);
        Assert.True(earlier.Moment < later.Moment);
    }

    [Theory]
    [InlineData("Unknown/City")]
    [InlineData("Europe/Moscow ")]
    public void Validate_UnknownTimezone_IsRejected(string name)
    {
        // Arrange
        var value = name;

        // Act
        var create = () => new UserTimeZone(value);

        // Assert
        Assert.Throws<ArgumentException>(() => create());
    }

    [Fact]
    public void Validate_Hash_IsIndependentOfCultureAndChangesWhenPayloadChanges()
    {
        // Arrange
        var previous = CultureInfo.CurrentCulture;
        var zone = new UserTimeZone("Europe/Moscow");
        var payload = new ReminderPayload(AgentChecks.ReminderText("  Проверить расписание  "),
            new ReminderSchedule(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.FromHours(3)), zone));
        var hasher = new ReminderPayloadHasher();

        // Act
        string first;
        string second;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            first = hasher.Hash(payload);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            second = hasher.Hash(payload);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Assert
        Assert.Equal(first, second);
        Assert.NotEqual(first, hasher.Hash(payload with
        {
            Text = AgentChecks.ReminderText("Другой текст"),
        }));
        Assert.Equal("Проверить расписание", payload.Text);
    }

    [Fact]
    public void Validate_Preparation_RejectsPastTimeAndChangedTimezone()
    {
        // Arrange
        var zone = new UserTimeZone("Europe/Moscow");
        var moment = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.FromHours(3));
        var payload = new ReminderPayload(AgentChecks.ReminderText("Проверить расписание"), new ReminderSchedule(moment, zone));
        var policy = new PreparedActionPolicy();

        // Act
        var past = policy.ValidatePreparation(zone, payload, moment);
        var changed = policy.ValidatePreparation(new UserTimeZone("Asia/Tbilisi"), payload, moment.AddHours(-1));

        // Assert
        Assert.Equal(ErrorCode.ReminderTimeInPast, past!.Code);
        Assert.Equal(ErrorCode.ActionMismatch, changed!.Code);
    }
}
