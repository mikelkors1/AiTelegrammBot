using ItmoBot.Application.ResultTypes;
using ItmoBot.Infrastructure.PostgreSql;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Unit.PostgreSql;

public sealed class DatabaseFailureTests
{
    [Fact]
    public async Task ConnectionRefusalReturnsSafeRetryableFailure()
    {
        // Arrange
        var port = new TestValues().FreePort();
        var settings = new TestValues().Settings(new Dictionary<string, string> { ["POSTGRES_PORT"] = port.Value.ToString() });
        await using var database = new Database(settings);

        // Act
        var actualResult = await database.CheckAsync(CancellationToken.None);

        // Assert
        var failure = Assert.IsType<DatabaseCheckResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.DatabaseUnavailable, failure.Error.Code);
        Assert.True(failure.Error.IsRetryable);
        Assert.DoesNotContain("db-secret", failure.Message);
    }
}
