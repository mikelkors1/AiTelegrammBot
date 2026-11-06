using ItmoBot.Configuration.Loading;
using ItmoBot.Infrastructure.Logging;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.Support;
using ItmoBot.Tools;
using Xunit;

namespace ItmoBot.Tests.Unit.Tools;

public sealed class LocalDevelopmentTests
{
    [Fact]
    public async Task Run_MissingProgram_ReturnsNamedFailure()
    {
        // Arrange

        // Act
        Result result = await new ProcessRunner().RunAsync("itmo-nonexistent-" + Guid.NewGuid(), [],
            "Проверка внешней программы", new SecretRedactor(new TestValues().Settings()));

        // Assert
        Result.Failure failure = Assert.IsType<Result.Failure>(result);
        Assert.Equal(ErrorCode.ProcessUnavailable, failure.Error.Code);
        Assert.Contains("PATH", failure.Error.Message);
    }

    [Fact]
    public void Run_InvalidEnv_IsRejectedBeforePromptOrWrite()
    {
        // Arrange
        var path = Path.GetTempFileName();
        try
        {
            const string original = "INVALID='secret-value\n";
            File.WriteAllText(path, original);

            // Act
            Result actualResult = new EnvironmentPreparer(new EnvironmentFileReader(), new SettingsLoader(new EnvironmentFileReader()), new SecretConsoleInput()).Prepare(path);

            // Assert
            Result.Failure failure = Assert.IsType<Result.Failure>(actualResult);
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
