using ItmoBot.Hosting;
using ItmoBot.Hosting.ResultTypes;
using ItmoBot.Shared.Errors;
using Xunit;

namespace ItmoBot.Tests.Unit.Hosting;

public sealed class CommandLineParserTests
{
    [Fact]
    public void RecognizesCommandAndCustomEnvFile()
    {
        // Arrange

        // Act
        var actualResult = new CommandLineParser().Parse(["healthcheck", "--env-file", "custom.env"]);

        // Assert
        var success = Assert.IsType<CommandLineParseResult.Success>(actualResult);
        Assert.Equal(ApplicationCommand.Healthcheck, success.Options.Command);
        Assert.Equal("custom.env", success.Options.EnvFile);
    }

    [Fact]
    public void RecognizesMigrationCommand()
    {
        // Arrange

        // Act
        var actualResult = new CommandLineParser().Parse(["migrate"]);

        // Assert
        var success = Assert.IsType<CommandLineParseResult.Success>(actualResult);
        Assert.Equal(ApplicationCommand.Migrate, success.Options.Command);
    }

    [Theory]
    [InlineData("--env-file")]
    [InlineData("invalid-secret")]
    public void InvalidArgumentsReturnSafeFailure(string arg)
    {
        // Arrange

        // Act
        var actualResult = new CommandLineParser().Parse([arg]);

        // Assert
        var failure = Assert.IsType<CommandLineParseResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.InvalidArguments, failure.Error.Code);
        Assert.DoesNotContain("invalid-secret", failure.Message);
    }
}
