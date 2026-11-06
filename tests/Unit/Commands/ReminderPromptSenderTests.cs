using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Reminders;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Commands;

public sealed class ReminderPromptSenderTests
{
    [Fact]
    public async Task SendAsync_ExpiredPendingAction_DoesNotDisplayFreshConfirmationButtons()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var telegram = new FakeTelegram();
        var action = new TrustedActionTestValues().Action(clock) with
        {
            Status = PreparedActionStatus.Pending
        };
        clock.Advance(TimeSpan.FromMinutes(5));
        var sender = new ReminderPromptSender(telegram, new ReminderCallbackCodec(), clock);

        // Act
        Result<int> result = await sender.SendAsync(action, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ActionExpired, Assert.IsType<Result<int>.Failure>(result).Error.Code);
        Assert.Empty(telegram.InlineKeyboards);
        Assert.Empty(telegram.Sent);
    }
}
