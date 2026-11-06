using ItmoBot.Configuration.Loading;
using ItmoBot.Configuration.Models;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Configuration;

public sealed class SettingsLoaderTests
{
    [Fact]
    public void Load_Environment_OverridesFileAndPreservesLiteralSecrets()
    {
        // Arrange
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, $"BOT_TOKEN={TestValues.Token}\nPOSTGRES_PASSWORD='p$a#ss'\nPOSTGRES_PORT=5432\nOPENROUTER_API_KEY=test-only-openrouter-secret\nLLM_API_BASE_URL=https://openrouter.ai/api/v1/\nLLM_MODEL=qwen/qwen3.8-27b:free\n");

            // Act
            Result<Settings> result = new SettingsLoader(new EnvironmentFileReader()).Load(file, new Dictionary<string, string> { ["POSTGRES_PORT"] = "55432" });

            // Assert
            var settings = Assert.IsType<Result<Settings>.Success>(result).Value;
            Assert.Equal(55432, settings.PostgresPort);
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
    [InlineData("MCP_TIMEOUT_SECONDS", "0", ErrorCode.InvalidConfiguration)]
    [InlineData("MCP_TIMEOUT_SECONDS", "16", ErrorCode.InvalidConfiguration)]
    [InlineData("MCP_TIMEOUT_SECONDS", "invalid", ErrorCode.InvalidConfiguration)]
    [InlineData("MCP_SERVER_PATH", "", ErrorCode.InvalidConfiguration)]
    [InlineData("MCP_SERVER_PATH", "server.exe", ErrorCode.InvalidConfiguration)]
    [InlineData("MCP_SERVER_PATH", "server\n.dll", ErrorCode.InvalidConfiguration)]
    [InlineData("SCHEDULE_FILE", "", ErrorCode.InvalidConfiguration)]
    [InlineData("SCHEDULE_TIMEZONE", "", ErrorCode.InvalidConfiguration)]
    [InlineData("SCHEDULE_TIMEZONE", "Mars/Olympus", ErrorCode.InvalidConfiguration)]
    [InlineData("WEATHER_HTTP_TIMEOUT_SECONDS", "0", ErrorCode.InvalidConfiguration)]
    [InlineData("WEATHER_HTTP_TIMEOUT_SECONDS", "6", ErrorCode.InvalidConfiguration)]
    [InlineData("WEATHER_HTTP_MAX_ATTEMPTS", "0", ErrorCode.InvalidConfiguration)]
    [InlineData("WEATHER_HTTP_MAX_ATTEMPTS", "3", ErrorCode.InvalidConfiguration)]
    [InlineData("WEATHER_HTTP_MAX_ATTEMPTS", "invalid", ErrorCode.InvalidConfiguration)]
    [InlineData("LLM_STRUCTURED_OUTPUT_MODE", "unknown", ErrorCode.InvalidConfiguration)]
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
    [InlineData("LLM_MAX_OUTPUT_TOKENS", "32768", ErrorCode.InvalidConfiguration)]
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
    public void Load_InvalidSetting_ReturnsNamedSafeFailure(string key, string value, ErrorCode code)
    {
        // Arrange
        var values = new TestValues().Environment();
        values[key] = value;

        // Act
        Result<Settings> result = new SettingsLoader(new EnvironmentFileReader()).Load("/nonexistent/env", values);

        // Assert
        Result<Settings>.Failure failure = Assert.IsType<Result<Settings>.Failure>(result);
        Assert.Equal(code, failure.Error.Code);
        Assert.Contains(key, failure.Message);
        Assert.DoesNotContain("invalid-secret", failure.Message);
        Assert.DoesNotContain(TestValues.Token, failure.Message);
    }

    [Theory]
    [InlineData("OPENROUTER_API_KEY", ErrorCode.InvalidLlmApiKey)]
    [InlineData("LLM_API_BASE_URL", ErrorCode.InvalidLlmApiAddress)]
    [InlineData("LLM_MODEL", ErrorCode.InvalidModelName)]
    public void Load_MissingRequiredLlmSetting_ReturnsSafeNamedFailure(string key, ErrorCode code)
    {
        // Arrange
        var values = new TestValues().Environment();
        values.Remove(key);

        // Act
        Result<Settings> result = new SettingsLoader(new EnvironmentFileReader()).Load("/nonexistent/env", values);

        // Assert
        Result<Settings>.Failure failure = Assert.IsType<Result<Settings>.Failure>(result);
        Assert.Equal(code, failure.Error.Code);
        Assert.Contains(key, failure.Message);
        Assert.DoesNotContain("test-only-openrouter-secret", failure.Message);
    }

    [Fact]
    public void Load_OptionalSettingsOmitted_AppliesDefaultsAndNormalizesAddress()
    {
        // Arrange

        // Act
        var settings = new TestValues().Settings(new Dictionary<string, string>
        {
            ["LLM_API_BASE_URL"] = "https://openrouter.ai/api/v1",
        });

        // Assert
        Assert.Equal("https://openrouter.ai/api/v1/", settings.Llm.ApiAddress.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.Llm.Timeout);
        Assert.Equal(2048, settings.Llm.MaxOutputTokens);
        Assert.Equal(32768, settings.Llm.ContextTokenLimit);
        Assert.Equal(24, settings.Llm.HistoryMessageLimit);
        Assert.Equal("[скрыто]", settings.Llm.ApiKey.ToString());
    }

    [Fact]
    public void Load_MalformedEnv_ReturnsLineNumberWithoutInputText()
    {
        // Arrange
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "# comment\nTOKEN='secret-not-closed\n");

            // Act
            Result<IReadOnlyDictionary<string, string>> actualResult = new EnvironmentFileReader().Read(file);

            // Assert
            Result<IReadOnlyDictionary<string, string>>.Failure failure = Assert.IsType<Result<IReadOnlyDictionary<string, string>>.Failure>(actualResult);
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
    public void Load_MissingFile_CanUseEnvironmentOnly()
    {
        // Arrange

        // Act
        Result<Settings> actualResult = new SettingsLoader(new EnvironmentFileReader()).Load("/nonexistent/env", new TestValues().Environment());

        // Assert
        Assert.IsType<Result<Settings>.Success>(actualResult);
        Result<Settings>.Failure failure = Assert.IsType<Result<Settings>.Failure>(new SettingsLoader(new EnvironmentFileReader()).Load("/nonexistent/env", new Dictionary<string, string>()));
        Assert.Equal(ErrorCode.InvalidToken, failure.Error.Code);
    }
    [Fact]
    public void Load_Loader_PropagatesFailureFromInjectedEnvironmentReader()
    {
        // Arrange
        var error = new AppError(ErrorCode.EnvironmentFileUnavailable, "Тестовая ошибка чтения.");
        var reader = new FakeEnvironmentFileReader(new Result<IReadOnlyDictionary<string, string>>.Failure(error));

        // Act
        Result<Settings> result = new SettingsLoader(reader).Load("unused.env", new Dictionary<string, string>());

        // Assert
        Assert.Same(error, Assert.IsType<Result<Settings>.Failure>(result).Error);
        Assert.Equal(1, reader.ReadCalls);
    }
    [Theory]
    [InlineData("json_schema", LlmStructuredOutputMode.JsonSchema)]
    [InlineData("json_object", LlmStructuredOutputMode.JsonObject)]
    [InlineData("tool_compatible_json", LlmStructuredOutputMode.ToolCompatibleJson)]
    public void Load_SupportedOutputMode_ReturnsConfiguredMode(string text, LlmStructuredOutputMode expected)
    {
        // Arrange
        var values = new TestValues().Environment();
        values["LLM_STRUCTURED_OUTPUT_MODE"] = text;

        // Act
        Result<Settings> loaded = new SettingsLoader(new EnvironmentFileReader()).Load("/missing-test-env", values);

        // Assert
        Assert.Equal(expected, Assert.IsType<Result<Settings>.Success>(loaded).Value.Llm.StructuredOutputMode);
    }


}
