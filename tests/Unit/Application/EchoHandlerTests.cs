using ItmoBot.Application.Messaging;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Application;

public sealed class EchoHandlerTests
{
    [Theory]
    [InlineData("Привет 👋")]
    [InlineData("/start")]
    [InlineData("<b>текст</b> & *слово*")]
    [InlineData("строка\nдва")]
    public async Task HandleAsync_Echo_PreservesText(string text)
    {
        // Arrange
        var telegram = new FakeTelegram();

        // Act
        Result<bool> result = await new EchoHandler(telegram).HandleAsync(Message(text), CancellationToken.None);

        // Assert
        Assert.True(Assert.IsType<Result<bool>.Success>(result).Value);
        Assert.Equal((42L, text), Assert.Single(telegram.Sent));
    }

    [Fact]
    public async Task HandleAsync_Caption_IsIgnored()
    {
        // Arrange
        var telegram = new FakeTelegram();

        // Act
        Result<bool> result = await new EchoHandler(telegram).HandleAsync(new IncomingUpdate(AgentChecks.UpdateId(0), AgentChecks.ChatId(42), null), CancellationToken.None);

        // Assert
        Assert.False(Assert.IsType<Result<bool>.Success>(result).Value);
        Assert.Empty(telegram.Sent);
    }

    [Fact]
    public async Task HandleAsync_ApiFailure_KeepsItsCodeAndRetryability()
    {
        // Arrange
        var error = new AppError(ErrorCode.TelegramUnauthorized, "Telegram отклонил токен.");
        var telegram = new FakeTelegram { SendError = error };

        // Act
        Result<bool> result = await new EchoHandler(telegram).HandleAsync(Message("Привет"), CancellationToken.None);

        // Assert
        Assert.Equal(error, Assert.IsType<Result<bool>.Failure>(result).Error);
        Assert.Empty(telegram.Sent);
    }

    [Fact]
    public async Task HandleAsync_CallerCancellation_IsNotReportedAsFailure()
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
        return new IncomingUpdate(AgentChecks.UpdateId(0), AgentChecks.ChatId(42), TelegramChecks.TelegramText(text));
    }
}
