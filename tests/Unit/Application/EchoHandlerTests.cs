using ItmoBot.Application.ValueObjects;
using ItmoBot.Application.Handlers;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Application.Models;
using Xunit;

namespace ItmoBot.Tests.Unit.Application;

public sealed class EchoHandlerTests
{
    [Theory]
    [InlineData("Привет 👋")]
    [InlineData("/start")]
    [InlineData("<b>текст</b> & *слово*")]
    [InlineData("строка\nдва")]
    public async Task EchoPreservesText(string text)
    {
        // Arrange
        var telegram = new FakeTelegram();

        // Act
        var result = await new EchoHandler(telegram).HandleAsync(Message(text), CancellationToken.None);

        // Assert
        Assert.True(Assert.IsType<MessageHandlingResult.Success>(result).WasHandled);
        Assert.Equal((42L, text), Assert.Single(telegram.Sent));
    }

    [Fact]
    public async Task CaptionIsIgnored()
    {
        // Arrange
        var telegram = new FakeTelegram();

        // Act
        var result = await new EchoHandler(telegram).HandleAsync(new IncomingUpdate(new UpdateId(0), new ChatId(42), null), CancellationToken.None);

        // Assert
        Assert.False(Assert.IsType<MessageHandlingResult.Success>(result).WasHandled);
        Assert.Empty(telegram.Sent);
    }

    [Fact]
    public async Task ApiFailureKeepsItsCodeAndRetryability()
    {
        // Arrange
        var error = new AppError(ErrorCode.TelegramUnauthorized, "Telegram отклонил токен.");
        var telegram = new FakeTelegram { SendError = error };

        // Act
        var result = await new EchoHandler(telegram).HandleAsync(Message("Привет"), CancellationToken.None);

        // Assert
        Assert.Equal(error, Assert.IsType<MessageHandlingResult.Failure>(result).Error);
        Assert.Empty(telegram.Sent);
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsFailure()
    {
        // Arrange
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        var telegram = new FakeTelegram();

        // Act
        Func<Task> act = () => new EchoHandler(telegram).HandleAsync(Message("Привет"), stopping.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            act);
        Assert.Empty(telegram.Sent);
    }

    private IncomingUpdate Message(string text)
    {
        return new IncomingUpdate(new UpdateId(0), new ChatId(42), new TelegramText(text));
    }
}
