using System.Text.Json;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Conversations.Context;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Mcp;
using ItmoBot.Mcp.Protocol;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using ModelContextProtocol.Protocol;

namespace ItmoBot.Tests.Support;

internal sealed class AgentLoopFixture
{
    public AgentLoopFixture(bool available = true, int retrySeconds = 600)
    {
        var definitions = new Tool[] { ScheduleMcpEndpoint.GetTool(), WeatherMcpEndpoint.GetTool(), ReminderMcpEndpoint.GetTool() }
            .Select(tool => new McpToolDefinition(AgentChecks.McpToolName(tool.Name), tool.Description,
                RequestChecks.McpObjectSchema(tool.InputSchema), RequestChecks.McpObjectSchema(tool.OutputSchema!.Value), tool.Annotations?.ReadOnlyHint == true)).ToArray();
        Runtime = new StubAgentRuntime(available ? new Result<McpCatalog>.Success(new McpCatalog(definitions, []))
            : new Result<McpCatalog>.Failure(new ItmoBot.Shared.Errors.AppError(ItmoBot.Shared.Errors.ErrorCode.McpUnavailable, "Недоступно")));
        Runtime.OnCall = _ => new Result<McpToolOutput>.Success(new McpToolOutput(Json("""
            {"date":"2026-10-12","timezone":"Europe/Moscow","lessons":[],"source":"repository_schedule"}
            """), false));
        Llm = new FakeLlmClient();
        Audit = new RecordingAgentAuditWriter();
        Proposals = new RecordingReminderProposal();
        Clock = new ManualTimeProvider();
        Clock.Advance(new DateTimeOffset(2026, 10, 12, 6, 0, 0, TimeSpan.Zero) - DateTimeOffset.UnixEpoch);
        Requests = new AgentRequestBuilder(Clock, new Utf8ContextTokenEstimator(),
            new ContextBudget(new TestValues().Settings().Llm.ContextTokenLimit, RequestChecks.TokenLimit(2048), RequestChecks.HistoryMessageLimit(24)), new ItmoBot.Application.AgentLoop.AgentReplyParser(new ToolSchemaValidator()));
        Service = new AgentCompletionService(Llm, Runtime, new StubAgentUsers(), Requests, new ToolSchemaValidator(), Proposals, Audit, Clock, new ItmoBot.Application.AgentLoop.AgentReplyParser(new ToolSchemaValidator()), RequestChecks.LlmRetryWindow(retrySeconds));
    }

    public AgentRequestBuilder Requests
    {
        get;
    }

    public StubAgentRuntime Runtime
    {
        get;
    }
    public FakeLlmClient Llm
    {
        get;
    }
    public RecordingAgentAuditWriter Audit
    {
        get;
    }
    public RecordingReminderProposal Proposals
    {
        get;
    }
    public ManualTimeProvider Clock
    {
        get;
    }
    public AgentCompletionService Service
    {
        get;
    }

    public JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public LlmToolCall Call(string id = "call_1", string name = "get_schedule", string arguments = "{\"date\":\"2026-10-12\"}")
    {
        return new LlmToolCall(RequestChecks.LlmToolCallId(id), AgentChecks.McpToolName(name), Json(arguments));
    }

    public void Replies(params LlmCompletionResult[] replies)
    {
        var queue = new Queue<LlmCompletionResult>(replies);
        Llm.OnComplete = (_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(queue.Dequeue());
        };
    }

    public LlmCompletionResult Answer(string text = "По данным расписания занятий нет.", bool clarify = false)
    {
        return new LlmCompletionResult.Success(new LlmResponse(RequestChecks.LlmText(JsonSerializer.Serialize(new
        {
            action = clarify ? "clarify" : "respond",
            text,
            reason = "Проверенная причина решения"
        })), RequestChecks.ModelName("test/model"), null, null));
    }

    public LlmRequest Request()
    {
        return new LlmRequest([new LlmMessage(LlmMessageRole.System, RequestChecks.LlmText("Помогай с учёбой.")),
            new LlmMessage(LlmMessageRole.User, RequestChecks.LlmText("Покажи расписание."))], RequestChecks.Temperature(0.0m));
    }
}
