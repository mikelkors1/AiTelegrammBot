using ItmoBot.Tests.TestDoubles;
using ItmoBot.Configuration.Loading;
using ItmoBot.Configuration.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Unit.Configuration;

public sealed class SettingsLoaderTests
{
    [Fact]
    public void EnvironmentOverridesFileAndPreservesLiteralSecrets()
    {
        // Arrange
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, $"BOT_TOKEN={TestValues.Token}\nPOSTGRES_PASSWORD='p$a#ss'\nPOSTGRES_PORT=5432\nOPENROUTER_API_KEY=test-only-openrouter-secret\nLLM_API_BASE_URL=https://openrouter.ai/api/v1/\nLLM_MODEL=qwen/qwen3.8-27b:free\n");

            // Act
            var result = new SettingsLoader(new EnvironmentFileReader()).Load(file, new Dictionary<string, string> { ["POSTGRES_PORT"] = "55432" });

            // Assert
            var settings = Assert.IsType<SettingsLoadResult.Success>(result).Settings;
            Assert.Equal(55432, settings.PostgresPort.Value);
            Assert.Equal("p$a#ss", settings.PostgresPassword.Value);
            Assert.DoesNotContain("p$a#ss", settings.ToString());
            Assert.DoesNotContain(TestValues.Token, settings.BotToken.ToString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("OPENROUTER_API_KEY", "", ErrorCode.InvalidLlmApiKey)]
    [InlineData("OPENROUTER_API_KEY", "invalid-secret\n", ErrorCode.InvalidLlmApiKey)]
    [InlineData("LLM_API_BASE_URL", "", ErrorCode.InvalidLlmApiAddress)]
    [InlineData("LLM_API_BASE_URL", "https://user:invalid-secret@host/api/", ErrorCode.InvalidLlmApiAddress)]
    [InlineData("LLM_API_BASE_URL", "https://host/api/?key=invalid-secret", ErrorCode.InvalidLlmApiAddress)]
    [InlineData("LLM_API_BASE_URL", "http://remote.example/api/", ErrorCode.InvalidLlmApiAddress)]
    [InlineData("LLM_MODEL", "", ErrorCode.InvalidModelName)]
    [InlineData("LLM_MODEL", "invalid-secret\n", ErrorCode.InvalidModelName)]
    [InlineData("LLM_TIMEOUT_SECONDS", "301", ErrorCode.InvalidConfiguration)]
    [InlineData("LLM_TIMEOUT_SECONDS", "0", ErrorCode.InvalidConfiguration)]
    [InlineData("LLM_RETRY_WINDOW_SECONDS", "0", ErrorCode.InvalidConfiguration)]
    [InlineData("LLM_RETRY_WINDOW_SECONDS", "601", ErrorCode.InvalidConfiguration)]
    [InlineData("LLM_RETRY_WINDOW_SECONDS", "abc", ErrorCode.InvalidConfiguration)]
    [InlineData("LLM_MAX_OUTPUT_TOKENS", "0", ErrorCode.InvalidConfiguration)]
    [InlineData("LLM_MAX_OUTPUT_TOKENS", "8192", ErrorCode.InvalidConfiguration)]
    [InlineData("HISTORY_MAX_TOKENS", "abc", ErrorCode.InvalidConfiguration)]
    [InlineData("HISTORY_MAX_MESSAGES", "-1", ErrorCode.InvalidConfiguration)]
    [InlineData("POSTGRES_PORT", "0", ErrorCode.InvalidPort)]
    [InlineData("HEALTH_PORT", "65536", ErrorCode.InvalidPort)]
    [InlineData("POSTGRES_PORT", "abc", ErrorCode.InvalidPort)]
    [InlineData("BOT_TOKEN", "invalid-secret", ErrorCode.InvalidToken)]
    [InlineData("POSTGRES_PASSWORD", "", ErrorCode.InvalidConfiguration)]
    [InlineData("LOG_LEVEL", "invalid-secret", ErrorCode.InvalidConfiguration)]
    [InlineData("TELEGRAM_PROXY_URL", "ftp://user:invalid-secret@host:123", ErrorCode.InvalidProxy)]
    [InlineData("TELEGRAM_PROXY_URL", "http://user:invalid-secret@host", ErrorCode.InvalidProxy)]
    [InlineData("POSTGRES_HOST", " ", ErrorCode.InvalidConfiguration)]
    [InlineData("POSTGRES_HOST", "", ErrorCode.InvalidConfiguration)]
    [InlineData("POSTGRES_DB", "", ErrorCode.InvalidConfiguration)]
    [InlineData("POSTGRES_USER", "invalid-secret\n", ErrorCode.InvalidConfiguration)]
    public void InvalidSettingReturnsNamedSafeFailure(string key, string value, ErrorCode code)
    {
        // Arrange
        var values = new TestValues().Environment();
        values[key] = value;

        // Act
        var result = new SettingsLoader(new EnvironmentFileReader()).Load("/nonexistent/env", values);

        // Assert
        var failure = Assert.IsType<SettingsLoadResult.Failure>(result);
        Assert.Equal(code, failure.Error.Code);
        Assert.Contains(key, failure.Message);
        Assert.DoesNotContain("invalid-secret", failure.Message);
        Assert.DoesNotContain(TestValues.Token, failure.Message);
    }

    [Theory]
    [InlineData("OPENROUTER_API_KEY", ErrorCode.InvalidLlmApiKey)]
    [InlineData("LLM_API_BASE_URL", ErrorCode.InvalidLlmApiAddress)]
    [InlineData("LLM_MODEL", ErrorCode.InvalidModelName)]
    public void MissingRequiredLlmSettingReturnsSafeNamedFailure(string key, ErrorCode code)
    {
        // Arrange
        var values = new TestValues().Environment();
        values.Remove(key);

        // Act
        var result = new SettingsLoader(new EnvironmentFileReader()).Load("/nonexistent/env", values);

        // Assert
        var failure = Assert.IsType<SettingsLoadResult.Failure>(result);
        Assert.Equal(code, failure.Error.Code);
        Assert.Contains(key, failure.Message);
        Assert.DoesNotContain("test-only-openrouter-secret", failure.Message);
    }

    [Fact]
    public void LlmDefaultsAndAddressNormalizationAreApplied()
    {
        // Arrange

        // Act
        var settings = new TestValues().Settings(new Dictionary<string, string>
        {
            ["LLM_API_BASE_URL"] = "https://openrouter.ai/api/v1",
        });

        // Assert
        Assert.Equal("https://openrouter.ai/api/v1/", settings.Llm.ApiAddress.Value.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.Llm.Timeout.Value);
        Assert.Equal(2048, settings.Llm.MaxOutputTokens.Value);
        Assert.Equal(8192, settings.Llm.ContextTokenLimit.Value);
        Assert.Equal(24, settings.Llm.HistoryMessageLimit.Value);
        Assert.Equal("[скрыто]", settings.Llm.ApiKey.ToString());
    }

    [Fact]
    public void MalformedEnvReturnsLineNumberWithoutInputText()
    {
        // Arrange
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "# comment\nTOKEN='secret-not-closed\n");

            // Act
            var actualResult = new EnvironmentFileReader().Read(file);

            // Assert
            var failure = Assert.IsType<EnvironmentReadResult.Failure>(actualResult);
            Assert.Equal(ErrorCode.EnvironmentFileInvalid, failure.Error.Code);
            Assert.Contains("строка 2", failure.Message);
            Assert.DoesNotContain("secret-not-closed", failure.Message);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void MissingFileCanUseEnvironmentOnly()
    {
        // Arrange

        // Act
        var actualResult = new SettingsLoader(new EnvironmentFileReader()).Load("/nonexistent/env", new TestValues().Environment());

        // Assert
        Assert.IsType<SettingsLoadResult.Success>(actualResult);
        var failure = Assert.IsType<SettingsLoadResult.Failure>(new SettingsLoader(new EnvironmentFileReader()).Load("/nonexistent/env", new Dictionary<string, string>()));
        Assert.Equal(ErrorCode.InvalidToken, failure.Error.Code);
    }
    [Fact]
    public void LoaderPropagatesFailureFromInjectedEnvironmentReader()
    {
        // Arrange
        var error = new AppError(ErrorCode.EnvironmentFileUnavailable, "Тестовая ошибка чтения.");
        var reader = new FakeEnvironmentFileReader(new EnvironmentReadResult.Failure(error));

        // Act
        var result = new SettingsLoader(reader).Load("unused.env", new Dictionary<string, string>());

        // Assert
        Assert.Same(error, Assert.IsType<SettingsLoadResult.Failure>(result).Error);
        Assert.Equal(1, reader.ReadCalls);
    }
}
