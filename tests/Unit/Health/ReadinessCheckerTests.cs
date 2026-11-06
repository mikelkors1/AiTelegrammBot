using ItmoBot.Infrastructure.Health;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Health;

public sealed class ReadinessCheckerTests
{
    [Theory]
    [InlineData("not-json-secret")]
    [InlineData("{\"status\":\"ok\"}")]
    [InlineData("{\"status\":\"ok\",\"database\":\"unavailable\",\"polling\":\"running\"}")]
    public async Task CheckAsync_IncorrectOrInconsistentBody_ReturnsSafeFailure(string body)
    {
        // Arrange
        using var checker = new ReadinessChecker(new HttpClient(new StaticHttpHandler(body)));

        // Act
        var actualResult = await checker.CheckAsync(new TestValues().Port(8080));

        // Assert
        var failure = Assert.IsType<Result.Failure>(actualResult);
        Assert.Equal(ErrorCode.HealthResponseInvalid, failure.Error.Code);
        Assert.DoesNotContain("not-json-secret", failure.Message);
    }

    [Fact]
    public async Task CheckAsync_Cancellation_RemainsCancellation()
    {
        // Arrange
        using var checker = new ReadinessChecker(new HttpClient(new StaticHttpHandler("{}")));
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();

        // Act
        Func<Task> act = () => checker.CheckAsync(new TestValues().Port(8080), stopping.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }
}
