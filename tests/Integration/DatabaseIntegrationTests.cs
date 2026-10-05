using ItmoBot.Application.ResultTypes;
using ItmoBot.Infrastructure.PostgreSql;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Integration;

public sealed class DatabaseIntegrationTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task IncorrectPasswordReturnsAuthenticationFailure()
    {
        // Arrange
        var settings = new TestValues().Settings(new Dictionary<string, string>
        {
            ["POSTGRES_HOST"] = Environment.GetEnvironmentVariable("TEST_POSTGRES_HOST") ?? "127.0.0.1",
            ["POSTGRES_PORT"] = Environment.GetEnvironmentVariable("TEST_POSTGRES_PORT") ?? "55432",
            ["POSTGRES_DB"] = "bot_test",
            ["POSTGRES_USER"] = "bot_test",
            ["POSTGRES_PASSWORD"] = "wrong-test-only-password",
        });
        await using var database = new Database(settings);

        // Act
        var actualResult = await database.CheckAsync(CancellationToken.None);

        // Assert
        var failure = Assert.IsType<DatabaseCheckResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.DatabaseAuthenticationFailed, failure.Error.Code);
        Assert.False(failure.Error.IsRetryable);
        Assert.DoesNotContain("wrong-test-only-password", failure.Message);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task PoolRunsSelectOneRepeatedly()
    {
        // Arrange
        var settings = new TestValues().Settings(new Dictionary<string, string>
        {
            ["POSTGRES_HOST"] = Environment.GetEnvironmentVariable("TEST_POSTGRES_HOST") ?? "127.0.0.1",
            ["POSTGRES_PORT"] = Environment.GetEnvironmentVariable("TEST_POSTGRES_PORT") ?? "55432",
            ["POSTGRES_DB"] = "bot_test",
            ["POSTGRES_USER"] = "bot_test",
            ["POSTGRES_PASSWORD"] = Environment.GetEnvironmentVariable("TEST_POSTGRES_PASSWORD") ?? "test-only-password",
        });
        await using var database = new Database(settings);

        // Act
        var actualResult = await database.CheckAsync(CancellationToken.None);

        // Assert
        Assert.IsType<DatabaseCheckResult.Success>(actualResult);
        Assert.IsType<DatabaseCheckResult.Success>(await database.CheckAsync(CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => database.CheckAsync(cancellation.Token));
    }
}
