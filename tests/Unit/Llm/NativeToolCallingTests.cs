using System.Text.Json;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Messaging;
using ItmoBot.Infrastructure.Llm;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.Support;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Llm;

public sealed class NativeToolCallingTests
{
    [Fact]
    public void ParseResponse_NativeToolResponse_AllowsNullContentAndKeepsCallIdentity()
    {
        // Arrange
        using var document = JsonDocument.Parse("""
            {"model":"test/model","choices":[{"finish_reason":"tool_calls","message":{"role":"assistant","content":null,"tool_calls":[
              {"id":"call_1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Казань\"}"}}]}}]}
            """);

        // Act
        var result = OpenRouterLlmClient.ParseResponse(document.RootElement);

        // Assert
        var call = Assert.Single(Assert.IsType<LlmCompletionResult.ToolCalls>(result).Calls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("Казань", call.Arguments.GetProperty("city").GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"not-json\"")]
    public void ParseResponse_MalformedToolArguments_ReturnFailure(string arguments)
    {
        // Arrange
        var root = JsonSerializer.SerializeToElement(new
        {
            model = "test/model",
            choices = new[]
            {
                new
                {
                    finish_reason = "tool_calls",
                    message = new
                    {
                        role = "assistant",
                        tool_calls = new[]
                        {
                            new { id = "call_1", type = "function", function = new { name = "get_schedule", arguments } },
                        },
                    },
                },
            },
        });

        // Act
        var result = OpenRouterLlmClient.ParseResponse(root);

        // Assert
        Assert.IsType<LlmCompletionResult.Failure>(result);
    }

    [Fact]
    public async Task CreateContent_Writer_SendsDefinitionsAndToolResultInNativeFormat()
    {
        // Arrange
        var fixture = new AgentLoopFixture();
        fixture.Replies(fixture.Answer());
        await fixture.Service.CompleteAsync(fixture.Request(), null, CancellationToken.None);
        var original = Assert.Single(fixture.Llm.Requests);
        var call = fixture.Call();
        var request = new LlmRequest(original.Messages.Concat(new[]
        {
            new LlmMessage(LlmMessageRole.Assistant, RequestChecks.LlmText("Вызов"), [call]),
            new LlmMessage(LlmMessageRole.Tool, RequestChecks.LlmText("{}"), toolCallId: call.Id),
        }), original.Temperature, original.Tools, original.FinalResponseSchema);

        // Act
        using var content = OpenRouterLlmClient.CreateContent(request, new TestValues().Settings().Llm);
        using var document = JsonDocument.Parse(await content.ReadAsStringAsync());

        // Assert
        var root = document.RootElement;
        Assert.Equal(3, root.GetProperty("tools").GetArrayLength());
        Assert.Equal("json_schema", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(3, root.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetProperty("required").GetArrayLength());
        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());
        Assert.False(root.TryGetProperty("parallel_tool_calls", out _));
        var messages = root.GetProperty("messages");
        Assert.Equal("call_1", messages[messages.GetArrayLength() - 1].GetProperty("tool_call_id").GetString());
        Assert.Equal("tool", messages[messages.GetArrayLength() - 1].GetProperty("role").GetString());
        Assert.Equal("get_schedule", messages[messages.GetArrayLength() - 2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task CreateContent_ToolCompatibleMode_RequiresNativeDecisionAndAllowsFinalOnlyJson()
    {
        // Arrange
        var fixture = new AgentLoopFixture();
        fixture.Replies(fixture.Answer());
        await fixture.Service.CompleteAsync(fixture.Request(), null, CancellationToken.None);
        var request = Assert.Single(fixture.Llm.Requests);
        var settings = new TestValues().Settings(new Dictionary<string, string>
        {
            ["LLM_STRUCTURED_OUTPUT_MODE"] = "tool_compatible_json",
        }).Llm;
        var finalRequest = new LlmRequest(request.Messages, request.Temperature, [], request.FinalResponseSchema);

        // Act
        using var toolContent = OpenRouterLlmClient.CreateContent(request, settings);
        using var toolDocument = JsonDocument.Parse(await toolContent.ReadAsStringAsync());
        using var finalContent = OpenRouterLlmClient.CreateContent(finalRequest, settings);
        using var finalDocument = JsonDocument.Parse(await finalContent.ReadAsStringAsync());
        Result<AgentFinalReply> invalid = new ItmoBot.Application.AgentLoop.AgentReplyParser(new ItmoBot.Application.Mcp.ToolSchemaValidator())
            .Parse(RequestChecks.LlmText("Обычный текст вместо JSON"));

        // Assert
        Assert.Equal(4, toolDocument.RootElement.GetProperty("tools").GetArrayLength());
        Assert.Equal("required", toolDocument.RootElement.GetProperty("tool_choice").GetString());
        Assert.False(toolDocument.RootElement.TryGetProperty("response_format", out _));
        Assert.Equal("json_object", finalDocument.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.IsType<Result<AgentFinalReply>.Failure>(invalid);
    }

    [Theory]
    [InlineData("get_schedule", true)]
    [InlineData("submit_final_reply", false)]
    public async Task CompleteAsync_NativeDecision_ReturnsToolOrValidatedFinalReply(string name, bool external)
    {
        // Arrange
        var fixture = new AgentLoopFixture();
        fixture.Replies(fixture.Answer());
        await fixture.Service.CompleteAsync(fixture.Request(), null, CancellationToken.None);
        var request = Assert.Single(fixture.Llm.Requests);
        const string code = "```csharp\nArray.Sort(values);\n```";
        var arguments = external ? "{\"date\":\"2026-11-02\"}"
            : JsonSerializer.Serialize(new
            {
                action = "respond",
                text = code,
                reason = "Учебный пример"
            });
        var transport = new ItmoBot.Tests.TestDoubles.LlmTransport { ResponseBody = NativeResponse(name, arguments) };
        using var client = NativeClient(fixture, transport);

        // Act
        var result = await client.CompleteAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(1, transport.Calls);
        if (external)
        {
            Assert.Equal(name, Assert.Single(Assert.IsType<LlmCompletionResult.ToolCalls>(result).Calls).Name);
        }
        else
        {
            var response = Assert.IsType<LlmCompletionResult.Success>(result).Response;
            var parsed = new AgentReplyParser(new ItmoBot.Application.Mcp.ToolSchemaValidator()).Parse(response.Text);
            Assert.Equal(code, Assert.IsType<Result<AgentFinalReply>.Success>(parsed).Value.Text);
        }
    }

    [Theory]
    [InlineData("text")]
    [InlineData("invalid_schema")]
    [InlineData("mixed_calls")]
    public async Task CompleteAsync_InvalidNativeDecision_RejectsWithoutExecutingOrFinalizingText(string scenario)
    {
        // Arrange
        var fixture = new AgentLoopFixture();
        fixture.Replies(fixture.Answer());
        await fixture.Service.CompleteAsync(fixture.Request(), null, CancellationToken.None);
        var request = Assert.Single(fixture.Llm.Requests);
        var body = scenario == "text"
            ? "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"get_weather(city=Казань)\"}}]}"
            : NativeResponse("submit_final_reply", scenario == "mixed_calls" ? "{\"action\":\"respond\",\"text\":\"Ответ\",\"reason\":\"Причина\"}" : "{}", scenario == "mixed_calls");
        var transport = new ItmoBot.Tests.TestDoubles.LlmTransport { ResponseBody = body };
        using var client = NativeClient(fixture, transport);

        // Act
        var result = await client.CompleteAsync(request, CancellationToken.None);

        // Assert
        Assert.IsType<LlmCompletionResult.Failure>(result);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task CompleteAsync_OversizedDecisionContext_DoesNotSendHttp()
    {
        // Arrange
        var fixture = new AgentLoopFixture();
        fixture.Replies(fixture.Answer());
        await fixture.Service.CompleteAsync(fixture.Request(), null, CancellationToken.None);
        var original = Assert.Single(fixture.Llm.Requests);
        var request = new LlmRequest([new LlmMessage(LlmMessageRole.User, new string('x', 40000))],
            original.Temperature, original.Tools, original.FinalResponseSchema);
        var transport = new ItmoBot.Tests.TestDoubles.LlmTransport();
        using var client = NativeClient(fixture, transport);

        // Act
        var result = await client.CompleteAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(ItmoBot.Shared.Errors.ErrorCode.ContextTooLarge, Assert.IsType<LlmCompletionResult.Failure>(result).Error.Code);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task CompleteAsync_ReadThenFinalReply_ReturnsMcpDataToModelAndDoesNotExecuteControlTool()
    {
        // Arrange
        var fixture = new AgentLoopFixture();
        var transport = new ItmoBot.Tests.TestDoubles.LlmTransport
        {
            ResponseBodies = [NativeResponse("get_schedule", "{\"date\":\"2026-10-12\"}"),
                NativeResponse("submit_final_reply", "{\"action\":\"respond\",\"text\":\"Занятий нет.\",\"reason\":\"Получено расписание\"}")],
        };
        using var client = NativeClient(fixture, transport);
        var parser = new AgentReplyParser(new ItmoBot.Application.Mcp.ToolSchemaValidator());
        var service = new AgentCompletionService(client, fixture.Runtime, new ItmoBot.Tests.TestDoubles.StubAgentUsers(),
            fixture.Requests, new ItmoBot.Application.Mcp.ToolSchemaValidator(), fixture.Proposals, fixture.Audit,
            fixture.Clock, parser, TimeSpan.FromMinutes(5));

        // Act
        var result = await service.CompleteAsync(fixture.Request(), null, CancellationToken.None);

        // Assert
        Assert.Equal("Занятий нет.", Assert.IsType<AgentCompletionResult.Success>(result).Response.Text);
        Assert.Single(fixture.Runtime.Calls);
        Assert.Equal(2, transport.Calls);
        using var body = JsonDocument.Parse(transport.Body!);
        var toolMessage = Assert.Single(body.RootElement.GetProperty("messages").EnumerateArray(),
            message => message.GetProperty("role").GetString() == "tool");
        Assert.Equal("call_1", toolMessage.GetProperty("tool_call_id").GetString());
        Assert.Contains("untrusted_data", toolMessage.GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_NativeSchemaExceedsRemainingBudget_TrimsHistoryAndSendsCurrentQuestion()
    {
        // Arrange
        var fixture = new AgentLoopFixture();
        var prompt = new ItmoBot.Application.Conversations.Prompts.StudyPrompt().Definition;
        var history = Enumerable.Range(0, 4).SelectMany(index => new[]
        {
            new LlmMessage(LlmMessageRole.User, $"history-user-{index}:" + new string('x', 1860)),
            new LlmMessage(LlmMessageRole.Assistant, $"history-answer-{index}:" + new string('y', 1860)),
        }).ToArray();
        var original = new LlmRequest(prompt.Messages.Concat(history).Append(new LlmMessage(LlmMessageRole.User, "Повтори ответ")),
            0m, mandatoryMessageCount: prompt.Messages.Count, historyMessageCount: history.Length);
        var catalog = Assert.IsType<Result<ItmoBot.Application.Mcp.McpCatalog>.Success>(await fixture.Runtime.GetCatalogAsync(CancellationToken.None)).Value;
        var request = fixture.Requests.Build(original, catalog.Tools, null, []);
        var transport = new ItmoBot.Tests.TestDoubles.LlmTransport
        {
            ResponseBody = NativeResponse("submit_final_reply", "{\"action\":\"respond\",\"text\":\"Ответ\",\"reason\":\"Учебный вопрос\"}"),
        };
        using var client = NativeClient(fixture, transport);

        // Act
        var result = await client.CompleteAsync(request, CancellationToken.None);

        // Assert
        Assert.IsType<LlmCompletionResult.Success>(result);
        Assert.Equal(1, transport.Calls);
        using var body = JsonDocument.Parse(transport.Body!);
        var messages = body.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Contains(messages, message => message.GetProperty("content").GetString() == "Повтори ответ");
        Assert.Contains(messages, message => message.GetProperty("content").GetString() == prompt.Messages[0].Content);
        Assert.True(messages.Count(message => message.GetProperty("content").GetString()!.StartsWith("history-", StringComparison.Ordinal)) < request.HistoryMessageCount);
    }

    [Fact]
    public async Task Build_LargeHistory_PreservesMandatoryTranslationExamplesAndToolContinuation()
    {
        // Arrange
        var fixture = new AgentLoopFixture();
        var prompt = new ItmoBot.Application.Conversations.Prompts.TranslatePrompt().Definition;
        var history = Enumerable.Range(0, 12).SelectMany(index => new[]
        {
            new LlmMessage(LlmMessageRole.User, $"history-user-{index}:" + new string('x', 4000)),
            new LlmMessage(LlmMessageRole.Assistant, $"history-answer-{index}:" + new string('y', 4000)),
        }).ToArray();
        var original = new LlmRequest(prompt.Messages.Concat(history).Append(new LlmMessage(LlmMessageRole.User, "Переведи ещё раз")),
            0m, mandatoryMessageCount: prompt.Messages.Count, historyMessageCount: history.Length);
        var catalog = Assert.IsType<Result<ItmoBot.Application.Mcp.McpCatalog>.Success>(await fixture.Runtime.GetCatalogAsync(CancellationToken.None)).Value;
        LlmMessage[] continuation =
        [
            new(LlmMessageRole.Assistant, "Запрошены инструменты.", [fixture.Call()]),
            new(LlmMessageRole.Tool, "Результат", toolCallId: "call_1"),
        ];

        // Act
        var request = fixture.Requests.Build(original, catalog.Tools, null, continuation);

        // Assert
        Assert.True(fixture.Requests.Fits(request));
        Assert.True(request.HistoryMessageCount < history.Length);
        Assert.Equal(0, request.HistoryMessageCount % 2);
        Assert.All(prompt.Messages, example => Assert.Contains(example, request.Messages));
        Assert.Contains(request.Messages, message => message.Content == "Переведи ещё раз");
        Assert.Equal(continuation, request.Messages.TakeLast(2));
    }

    private OpenRouterLlmClient NativeClient(AgentLoopFixture fixture, ItmoBot.Tests.TestDoubles.LlmTransport transport)
    {
        var settings = new TestValues().Settings(new Dictionary<string, string> { ["LLM_STRUCTURED_OUTPUT_MODE"] = "tool_compatible_json" }).Llm;
        return new OpenRouterLlmClient(settings, new HttpClient(transport), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, fixture.Requests);
    }

    private string NativeResponse(string name, string arguments, bool mixed = false)
    {
        var calls = new List<object> { new { id = "call_1", type = "function", function = new { name, arguments } } };
        if (mixed)
        {
            calls.Add(new
            {
                id = "call_2",
                type = "function",
                function = new
                {
                    name = "get_weather",
                    arguments = "{\"city\":\"Казань\"}"
                }
            });
        }

        return JsonSerializer.Serialize(new
        {
            model = "test/model",
            choices = new[] { new { finish_reason = "tool_calls", message = new { role = "assistant", tool_calls = calls } } }
        });
    }

    [Fact]
    public async Task CreateContent_JsonObjectMode_PreservesNativeToolsAndLocalSchemaRemainsStrict()
    {
        // Arrange
        var fixture = new AgentLoopFixture();
        fixture.Replies(fixture.Answer());
        await fixture.Service.CompleteAsync(fixture.Request(), null, CancellationToken.None);
        var request = Assert.Single(fixture.Llm.Requests);
        var settings = new TestValues().Settings(new Dictionary<string, string>
        {
            ["LLM_MODEL"] = "google/gemma-4-26b-a4b-it:free",
            ["LLM_STRUCTURED_OUTPUT_MODE"] = "json_object",
        }).Llm;

        // Act
        using var content = OpenRouterLlmClient.CreateContent(request, settings);
        using var document = JsonDocument.Parse(await content.ReadAsStringAsync());
        Result<AgentFinalReply> invalid = new ItmoBot.Application.AgentLoop.AgentReplyParser(new ItmoBot.Application.Mcp.ToolSchemaValidator())
            .Parse(RequestChecks.LlmText("{\"action\":\"respond\",\"text\":\"Нет reason\"}"));

        // Assert
        Assert.Equal("json_object", document.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("tools").GetArrayLength());
        Assert.False(document.RootElement.GetProperty("response_format").TryGetProperty("json_schema", out _));
        Assert.IsType<Result<AgentFinalReply>.Failure>(invalid);
    }

}
