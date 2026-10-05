using System.Net;
using System.Text.Json;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Llm;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Unit.Llm;

public sealed class OpenRouterLlmClientTests
{
    [Theory]
    [InlineData("0.0")]
    [InlineData("0.3")]
    [InlineData("0.7")]
    [InlineData("1.0")]
    public async Task RequestPreservesRolesOrderAndGenerationParameters(string temperature)
    {
        // Arrange
        var transport = new LlmTransport { ResponseBody = Response("Ответ") };
        using var client = CreateClient(transport);
        var creativity = new Temperature(decimal.Parse(temperature, System.Globalization.CultureInfo.InvariantCulture));
        var request = new LlmRequest([
            new LlmMessage(LlmMessageRole.System, new LlmText("Системная инструкция")),
            new LlmMessage(LlmMessageRole.User, new LlmText("Предыдущий запрос")),
            new LlmMessage(LlmMessageRole.Assistant, new LlmText("Предыдущий ответ")),
            new LlmMessage(LlmMessageRole.User, new LlmText("Текущий запрос")),
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
        Assert.Equal(creativity.Value, root.GetProperty("temperature").GetDecimal());
        Assert.Equal(2048, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("reasoning").GetProperty("enabled").GetBoolean());
        Assert.True(root.GetProperty("provider").GetProperty("require_parameters").GetBoolean());
        Assert.False(root.GetProperty("provider").GetProperty("allow_fallbacks").GetBoolean());
        Assert.Equal(new[] { "system", "user", "assistant", "user" }, root.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal(request.Messages.Select(m => m.Content.Value), root.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("content").GetString()));
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task CompleteResponsePreservesLongTextAndUsage()
    {
        // Arrange
        var text = "  " + new string('а', 5000) + "👋\n";
        var transport = new LlmTransport { ResponseBody = Response(text) };
        using var client = CreateClient(transport);

        // Act
        var result = await client.CompleteAsync(Request(), CancellationToken.None);

        // Assert
        var response = Assert.IsType<LlmCompletionResult.Success>(result).Response;
        Assert.Equal(text, response.Text.Value);
        Assert.Equal("qwen/qwen3.8-27b:free", response.Model.Value);
        Assert.Equal("ModelRun", response.Provider!.Value);
        Assert.Equal(10, response.Usage!.InputTokens.Value);
        Assert.Equal(20, response.Usage.OutputTokens.Value);
        Assert.Equal(30, response.Usage.TotalTokens.Value);
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
    public async Task HttpErrorIsClassifiedWithoutLeakingResponseBodyOrRetrying(int status, ErrorCode code, bool retryable)
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
    public async Task ErrorInsideHttp200IsNotTreatedAsSuccess()
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
    public async Task MalformedEmptyAndIncompleteResponsesHaveSafeFailures(string body, ErrorCode code)
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
    public async Task UsageIsOptionalButValidatedWhenPresent(string usage, bool success)
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
    public async Task NetworkErrorDoesNotExposeExceptionMessageInLogsOrResult()
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
    public async Task TimeoutReturnsRetryableFailure()
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
    public async Task CallerCancellationPropagatesInsteadOfReturningFailure()
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
    public void ClientDisposesOwnedHttpResources()
    {
        // Arrange
        var transport = new LlmTransport();
        var client = CreateClient(transport);

        // Act
        client.Dispose();

        // Assert
        Assert.True(transport.WasDisposed);
    }

    private OpenRouterLlmClient CreateClient(LlmTransport transport, Settings? settings = null, ILogger? logger = null)
    {
        var mapper = new LlmErrorMapper();
        return new OpenRouterLlmClient((settings ?? new TestValues().Settings()).Llm, new HttpClient(transport),
            new OpenRouterRequestWriter(), new OpenRouterResponseParser(mapper), mapper, logger ?? NullLogger.Instance);
    }

    private LlmRequest Request()
    {
        return new LlmRequest([new LlmMessage(LlmMessageRole.User, new LlmText("Учебный запрос"))], new Temperature(0.3m));
    }

    private string Response(string text, string usage = "{\"prompt_tokens\":10,\"completion_tokens\":20,\"total_tokens\":30}")
    {
        return "{\"model\":\"qwen/qwen3.8-27b:free\",\"provider\":\"ModelRun\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":"
            + JsonSerializer.Serialize(text) + "}}],\"usage\":" + usage + "}";
    }
}
