using ItmoBot.Configuration.Loading;
using ItmoBot.Infrastructure.Logging;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using ItmoBot.Tools;
using ItmoBot.Tools.ResultTypes;
using Xunit;

namespace ItmoBot.Tests.Unit.Tools;

public sealed class LocalDevelopmentTests
{
    [Fact]
    public async Task MissingProgramReturnsNamedFailure()
    {
        // Arrange

        // Act
        var result = await new ProcessRunner().RunAsync("itmo-nonexistent-" + Guid.NewGuid(), [],
            "Проверка внешней программы", new SecretRedactor(new TestValues().Settings()));

        // Assert
        var failure = Assert.IsType<CommandExecutionResult.Failure>(result);
        Assert.Equal(ErrorCode.ProcessUnavailable, failure.Error.Code);
        Assert.Contains("PATH", failure.Error.Message);
    }

    [Fact]
    public void InvalidEnvIsRejectedBeforePromptOrWrite()
    {
        // Arrange
        var path = Path.GetTempFileName();
        try
        {
            const string original = "INVALID='secret-value\n";
            File.WriteAllText(path, original);

            // Act
            var actualResult = new EnvironmentPreparer(new EnvironmentFileReader(), new SettingsLoader(new EnvironmentFileReader()), new SecretConsoleInput()).Prepare(path);

            // Assert
            var failure = Assert.IsType<EnvironmentPreparationResult.Failure>(actualResult);
            Assert.Equal(ErrorCode.EnvironmentFileInvalid, failure.Error.Code);
            Assert.DoesNotContain("secret-value", failure.Error.Message);
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
