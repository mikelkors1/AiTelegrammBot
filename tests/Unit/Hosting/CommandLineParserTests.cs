using ItmoBot.Hosting;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using Xunit;

namespace ItmoBot.Tests.Unit.Hosting;

public sealed class CommandLineParserTests
{
    [Fact]
    public void Parse_HealthcheckWithCustomEnvFile_ReturnsCommandAndPath()
    {
        // Arrange

        // Act
        Result<CommandLineOptions> actualResult = new CommandLineParser().Parse(["healthcheck", "--env-file", "custom.env"]);

        // Assert
        Result<CommandLineOptions>.Success success = Assert.IsType<Result<CommandLineOptions>.Success>(actualResult);
        Assert.Equal(ApplicationCommand.Healthcheck, success.Value.Command);
        Assert.Equal("custom.env", success.Value.EnvFile);
    }

    [Fact]
    public void Parse_MigrationArgument_ReturnsMigrationCommand()
    {
        // Arrange

        // Act
        Result<CommandLineOptions> actualResult = new CommandLineParser().Parse(["migrate"]);

        // Assert
        Result<CommandLineOptions>.Success success = Assert.IsType<Result<CommandLineOptions>.Success>(actualResult);
        Assert.Equal(ApplicationCommand.Migrate, success.Value.Command);
    }

    [Theory]
    [InlineData("--env-file")]
    [InlineData("invalid-secret")]
    public void Parse_InvalidArguments_ReturnSafeFailure(string arg)
    {
        // Arrange

        // Act
        Result<CommandLineOptions> actualResult = new CommandLineParser().Parse([arg]);

        // Assert
        Result<CommandLineOptions>.Failure failure = Assert.IsType<Result<CommandLineOptions>.Failure>(actualResult);
        Assert.Equal(ErrorCode.InvalidArguments, failure.Error.Code);
        Assert.DoesNotContain("invalid-secret", failure.Message);
    }
}
