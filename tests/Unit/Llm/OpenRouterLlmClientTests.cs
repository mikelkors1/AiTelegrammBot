using System.Net;
using System.Text.Json;
using ItmoBot.Application.Llm;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Llm;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ItmoBot.Tests.Unit.Llm;

public sealed class OpenRouterLlmClientTests
{
    [Theory]
    [InlineData("0.0")]
    [InlineData("0.3")]
    [InlineData("0.7")]
    [InlineData("1.0")]
    public async Task CompleteAsync_Request_PreservesRolesOrderAndGenerationParameters(string temperature)
    {
        // Arrange
        var transport = new LlmTransport { ResponseBody = Response("Ответ") };
        using var client = CreateClient(transport);
        decimal creativity = RequestChecks.Temperature(decimal.Parse(temperature, System.Globalization.CultureInfo.InvariantCulture));
        var request = new LlmRequest([
            new LlmMessage(LlmMessageRole.System, RequestChecks.LlmText("Системная инструкция")),
            new LlmMessage(LlmMessageRole.User, RequestChecks.LlmText("Предыдущий запрос")),
            new LlmMessage(LlmMessageRole.Assistant, RequestChecks.LlmText("Предыдущий ответ")),
            new LlmMessage(LlmMessageRole.User, RequestChecks.LlmText("Текущий запрос")),
        ], creativity);

        // Act
        var result = await client.CompleteAsync(request, CancellationToken.None);

        // Assert
        Assert.IsType<LlmCompletionResult.Success>(result);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", transport.Url!.AbsoluteUri);
        Assert.Equal("Bearer test-only-openrouter-secret", transport.Authorization);
        using var body = JsonDocument.Parse(transport.Body!);
        var root = body.RootElement;
        Assert.Equal("qwen/qwen3.8-27b:free", root.GetProperty("model").GetString());
        Assert.Equal(creativity, root.GetProperty("temperature").GetDecimal());
        Assert.Equal(2048, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("reasoning").GetProperty("enabled").GetBoolean());
        Assert.True(root.GetProperty("provider").GetProperty("require_parameters").GetBoolean());
        Assert.False(root.GetProperty("provider").GetProperty("allow_fallbacks").GetBoolean());
        Assert.Equal(new[] { "system", "user", "assistant", "user" }, root.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal(request.Messages.Select(m => m.Content), root.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("content").GetString()));
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task CompleteAsync_CompleteResponse_PreservesLongTextAndUsage()
    {
        // Arrange
        var text = "  " + new string('а', 5000) + "👋\n";
        var transport = new LlmTransport { ResponseBody = Response(text) };
        using var client = CreateClient(transport);

        // Act
        var result = await client.CompleteAsync(Request(), CancellationToken.None);

        // Assert
        var response = Assert.IsType<LlmCompletionResult.Success>(result).Response;
        Assert.Equal(text, response.Text);
        Assert.Equal("qwen/qwen3.8-27b:free", response.Model);
        Assert.Equal("ModelRun", response.Provider!);
        Assert.Equal(10, response.Usage!.InputTokens);
        Assert.Equal(20, response.Usage.OutputTokens);
        Assert.Equal(30, response.Usage.TotalTokens);
    }

    [Theory]
    [InlineData(401, ErrorCode.LlmUnauthorized, false)]
    [InlineData(402, ErrorCode.LlmCreditLimit, false)]
    [InlineData(403, ErrorCode.LlmForbidden, false)]
    [InlineData(408, ErrorCode.LlmTimeout, true)]
    [InlineData(429, ErrorCode.LlmRateLimited, true)]
    [InlineData(500, ErrorCode.LlmUnavailable, true)]
    [InlineData(503, ErrorCode.LlmUnavailable, true)]
    [InlineData(504, ErrorCode.LlmTimeout, true)]
    [InlineData(400, ErrorCode.LlmRejectedRequest, false)]
    public async Task CompleteAsync_HttpError_IsClassifiedWithoutLeakingResponseBodyOrRetrying(int status, ErrorCode code, bool retryable)
    {
        // Arrange
        var transport = new LlmTransport
        {
            Status = (HttpStatusCode)status,
            ResponseBody = "secret-user-dialog test-only-openrouter-secret http://user:password@host",
            RetryAfter = TimeSpan.FromSeconds(15),
        };
        using var client = CreateClient(transport);

        // Act
        var actualResult = await client.CompleteAsync(Request(), CancellationToken.None);

        // Assert
        var failure = Assert.IsType<LlmCompletionResult.Failure>(actualResult);

        Assert.Equal(code, failure.Error.Code);
        Assert.Equal(retryable, failure.Error.IsRetryable);
        Assert.Equal(TimeSpan.FromSeconds(15), failure.RetryAfter);
        Assert.DoesNotContain("secret", failure.Message);
        Assert.DoesNotContain("password", failure.Message);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task CompleteAsync_ErrorInsideHttp200_IsNotTreatedAsSuccess()
    {
        // Arrange
        var transport = new LlmTransport { ResponseBody = "{\"error\":{\"code\":429,\"message\":\"secret-dialog\"}}" };
        using var client = CreateClient(transport);

        // Act
        var actualResult = await client.CompleteAsync(Request(), CancellationToken.None);

        // Assert
        var failure = Assert.IsType<LlmCompletionResult.Failure>(actualResult);

        Assert.Equal(ErrorCode.LlmRateLimited, failure.Error.Code);
        Assert.True(failure.Error.IsRetryable);
        Assert.DoesNotContain("secret-dialog", failure.Message);
    }

    [Theory]
    [InlineData("invalid-json", ErrorCode.InvalidLlmResponse)]
    [InlineData("[]", ErrorCode.InvalidLlmResponse)]
    [InlineData("{}", ErrorCode.InvalidLlmResponse)]
    [InlineData("{\"choices\":[]}", ErrorCode.InvalidLlmResponse)]
    [InlineData("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"partial\"}}]}", ErrorCode.IncompleteLlmResponse)]
    [InlineData("{\"choices\":[{\"finish_reason\":\"content_filter\"}]}", ErrorCode.LlmForbidden)]
    [InlineData("{\"choices\":[{\"finish_reason\":\"tool_calls\"}]}", ErrorCode.InvalidLlmResponse)]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":null}}]}", ErrorCode.EmptyLlmResponse)]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\" \"}}]}", ErrorCode.EmptyLlmResponse)]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":{}}}]}", ErrorCode.InvalidLlmResponse)]
    public async Task CompleteAsync_MalformedEmptyAndIncompleteResponses_HaveSafeFailures(string body, ErrorCode code)
    {
        // Arrange
        var transport = new LlmTransport { ResponseBody = body };
        using var client = CreateClient(transport);

        // Act
        var actualResult = await client.CompleteAsync(Request(), CancellationToken.None);

        // Assert
        var failure = Assert.IsType<LlmCompletionResult.Failure>(actualResult);

        Assert.Equal(code, failure.Error.Code);
        Assert.DoesNotContain("partial", failure.Message);
        Assert.False(failure.Error.IsRetryable);
    }

    [Theory]
    [InlineData("null", true)]
    [InlineData("{\"prompt_tokens\":-1,\"completion_tokens\":20,\"total_tokens\":19}", false)]
    [InlineData("{\"prompt_tokens\":10,\"completion_tokens\":20,\"total_tokens\":99}", false)]
    [InlineData("{\"prompt_tokens\":10}", false)]
    public async Task CompleteAsync_Usage_IsOptionalButValidatedWhenPresent(string usage, bool success)
    {
        // Arrange
        var transport = new LlmTransport { ResponseBody = Response("Ответ", usage) };
        using var client = CreateClient(transport);

        // Act
        var result = await client.CompleteAsync(Request(), CancellationToken.None);

        if (success)
        {

            // Assert
            Assert.Null(Assert.IsType<LlmCompletionResult.Success>(result).Response.Usage);
        }
        else
        {
            Assert.Equal(ErrorCode.InvalidLlmResponse, Assert.IsType<LlmCompletionResult.Failure>(result).Error.Code);
        }
    }

    [Fact]
    public async Task CompleteAsync_NetworkError_DoesNotExposeExceptionMessageInLogsOrResult()
    {
        // Arrange
        var transport = new LlmTransport { Error = new HttpRequestException("test-only-openrouter-secret secret-user-dialog secret-system-prompt") };
        var logger = new RecordingLogger();
        using var client = CreateClient(transport, logger: logger);

        // Act
        var actualResult = await client.CompleteAsync(Request(), CancellationToken.None);

        // Assert
        var failure = Assert.IsType<LlmCompletionResult.Failure>(actualResult);

        Assert.Equal(ErrorCode.LlmUnavailable, failure.Error.Code);
        Assert.True(failure.Error.IsRetryable);
        var logs = string.Join("\n", logger.Entries);
        Assert.Contains("qwen/qwen3.8-27b:free", logs);
        Assert.Contains("длительность", logs);
        Assert.DoesNotContain("secret", logs + failure.Message);
        Assert.Equal(2, logger.Entries.Count);
    }

    [Fact]
    public async Task CompleteAsync_Timeout_ReturnsRetryableFailure()
    {
        // Arrange
        var transport = new LlmTransport { WaitForCancellation = true };
        using var client = CreateClient(transport, new TestValues().Settings(new Dictionary<string, string> { ["LLM_TIMEOUT_SECONDS"] = "1" }));

        // Act
        var actualResult = await client.CompleteAsync(Request(), CancellationToken.None);

        // Assert
        var failure = Assert.IsType<LlmCompletionResult.Failure>(actualResult);

        Assert.Equal(ErrorCode.LlmTimeout, failure.Error.Code);
        Assert.True(failure.Error.IsRetryable);
    }

    [Fact]
    public async Task CompleteAsync_CallerCancellation_PropagatesInsteadOfReturningFailure()
    {
        // Arrange
        var transport = new LlmTransport { WaitForCancellation = true };
        using var client = CreateClient(transport);
        using var stopping = new CancellationTokenSource();
        var running = client.CompleteAsync(Request(), stopping.Token);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        stopping.Cancel();

        // Act
        Func<Task> act = () => running;

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    [Fact]
    public void Dispose_OwnedHttpClient_DisposesTransport()
    {
        // Arrange
        var transport = new LlmTransport();
        var client = CreateClient(transport);

        // Act
        client.Dispose();

        // Assert
        Assert.True(transport.WasDisposed);
    }

    [Theory]
    [InlineData("daily", true)]
    [InlineData("legacy_daily", true)]
    [InlineData("upstream", false)]
    [InlineData("minute", false)]
    [InlineData("malformed", false)]
    public async Task CompleteAsync_RateLimitResponse_DistinguishesDailyQuotaWithoutLeakingBody(string kind, bool daily)
    {
        // Arrange
        var reset = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1), TimeSpan.Zero);
        var metadata = new Dictionary<string, object?>
        {
            ["headers"] = new Dictionary<string, string>
            {
                ["X-RateLimit-Limit"] = kind == "minute" ? "20" : "50",
                ["X-RateLimit-Remaining"] = "0",
                ["X-RateLimit-Reset"] = reset.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
        };
        if (kind == "daily")
        {
            metadata["limit_source"] = "openrouter_free_tier_daily";
        }
        else if (kind == "upstream")
        {
            metadata["provider_name"] = "provider";
        }

        var logger = new RecordingLogger();
        var transport = new LlmTransport
        {
            Status = HttpStatusCode.TooManyRequests,
            ResponseBody = kind == "malformed" ? "private-secret" : JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = 429,
                    message = "private-secret free-models-per-day",
                    metadata
                },
            }),
        };
        using var client = CreateClient(transport, logger: logger);

        // Act
        var result = await client.CompleteAsync(Request(), CancellationToken.None);

        // Assert
        var failure = Assert.IsType<LlmCompletionResult.Failure>(result);
        Assert.Equal(daily ? ErrorCode.LlmDailyQuotaExceeded : ErrorCode.LlmRateLimited, failure.Error.Code);
        Assert.Equal(!daily, failure.Error.IsRetryable);
        Assert.DoesNotContain("private-secret", failure.Message + string.Join("", logger.Entries));
        if (daily)
        {
            Assert.Equal(reset, failure.QuotaResetsAt);
            Assert.Contains(logger.Entries, line => line.Contains("Суточный лимит", StringComparison.Ordinal));
        }
    }

    private OpenRouterLlmClient CreateClient(LlmTransport transport, Settings? settings = null, ILogger? logger = null)
    {
        return new OpenRouterLlmClient((settings ?? new TestValues().Settings()).Llm, new HttpClient(transport), logger ?? NullLogger.Instance, new AgentLoopFixture().Requests);
    }

    private LlmRequest Request()
    {
        return new LlmRequest([new LlmMessage(LlmMessageRole.User, RequestChecks.LlmText("Учебный запрос"))], RequestChecks.Temperature(0.3m));
    }

    private string Response(string text, string usage = "{\"prompt_tokens\":10,\"completion_tokens\":20,\"total_tokens\":30}")
    {
        return "{\"model\":\"qwen/qwen3.8-27b:free\",\"provider\":\"ModelRun\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":"
            + JsonSerializer.Serialize(text) + "}}],\"usage\":" + usage + "}";
    }

    [Fact]
    public void CreateRequest_MutatingSourceHistory_DoesNotChangeAnAlreadyPreparedRequest()
    {
        // Arrange
        var original = new LlmMessage(LlmMessageRole.User, RequestChecks.LlmText("Первый запрос"));
        var history = new List<LlmMessage> { original };
        var request = new LlmRequest(history, RequestChecks.Temperature(0.0m));

        history.Clear();

        // Act
        history.Add(new LlmMessage(LlmMessageRole.User, RequestChecks.LlmText("Другой запрос")));

        // Assert
        Assert.Equal(original, Assert.Single(request.Messages));
        Assert.Throws<ArgumentException>(() => new LlmRequest([], RequestChecks.Temperature(0.0m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmMessage((LlmMessageRole)99, original.Content));
    }

}
