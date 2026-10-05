using ItmoBot.Application.Conversations;
using ItmoBot.Application.ValueObjects;
using Xunit;

namespace ItmoBot.Tests.Unit.Application;

public sealed class ConversationLockTests
{
    [Fact]
    public async Task SameChatWaitsWhileOtherChatCanProceed()
    {
        // Arrange
        var coordinator = new ConversationLock();
        var first = await coordinator.AcquireAsync(new ChatId(42), CancellationToken.None);
        var waiting = coordinator.AcquireAsync(new ChatId(42), CancellationToken.None);

        // Act
        await using var other = await coordinator.AcquireAsync(new ChatId(43), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.False(waiting.IsCompleted);

        await first.DisposeAsync();
        await using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelledWaiterDoesNotReleaseOwnersLock()
    {
        // Arrange
        var coordinator = new ConversationLock();
        var first = await coordinator.AcquireAsync(new ChatId(42), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var cancelled = coordinator.AcquireAsync(new ChatId(42), cancellation.Token);
        cancellation.Cancel();

        // Act
        Func<Task> act = () => cancelled;

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        var waiting = coordinator.AcquireAsync(new ChatId(42), CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        await first.DisposeAsync();
        await first.DisposeAsync();
        await using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        var third = coordinator.AcquireAsync(new ChatId(42), CancellationToken.None);
        Assert.False(third.IsCompleted);
        await second.DisposeAsync();
        await using var last = await third.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
