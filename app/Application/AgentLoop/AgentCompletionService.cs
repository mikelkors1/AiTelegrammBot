using System.Text.Json;
using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Mcp;
using ItmoBot.Application.Reminders;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Validation;

namespace ItmoBot.Application.AgentLoop;

public sealed class AgentCompletionService(ILlmClient llm, IMcpRuntime mcp, IAgentUserRepository users,
    AgentRequestBuilder requests, ToolSchemaValidator schemas, IReminderProposalHandler reminders,
    IAgentAuditWriter audit, TimeProvider clock, AgentReplyParser replies, TimeSpan retryWindow)
{
    public async Task<AgentCompletionResult> CompleteAsync(LlmRequest request, AgentTurnContext? context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Guid requestId = AgentChecks.AgentRequestId(Guid.NewGuid());
        using var deadline = new CancellationTokenSource(retryWindow, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            return await CompleteCoreAsync(request, context, requestId, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await audit.RecordAsync(context, requestId, AuditAction.Reject, AuditStatus.Failed, null, cleanup.Token).WaitAsync(cleanup.Token);
            }
            catch (Exception)
            {
                // Отказ аудита не изменяет результат: общий срок уже истёк.
            }

            return new AgentCompletionResult.Failure(new AppError(ErrorCode.LlmRetryTimeout, "Общее время обработки запроса истекло."));
        }
    }

    private async Task<AgentCompletionResult> CompleteCoreAsync(LlmRequest request, AgentTurnContext? context, Guid requestId, CancellationToken cancellationToken)
    {
        context = context is null ? null : context with
        {
            StartedAt = clock.GetUtcNow()
        };
        var discovered = await mcp.GetCatalogAsync(cancellationToken);
        var tools = discovered is Result<McpCatalog>.Success success
            ? success.Value.Tools.Where(tool => tool.Name is "get_weather" or "get_schedule" or "add_reminder").ToArray()
            : [];
        UserTimeZone? timezone = null;
        if (context is not null)
        {
            var saved = await users.GetTimeZoneAsync(context.Owner, cancellationToken);
            timezone = saved is AgentStorageResult<UserTimeZone?>.Success zone ? zone.Value : null;
        }

        context = context is null ? null : context with
        {
            TimeZone = timezone
        };
        var continuation = new List<LlmMessage>();
        var usedIds = new HashSet<string>();
        var usedCalls = 0;
        for (var round = 0; round < 3; round++)
        {
            var next = requests.Build(request, tools, timezone, continuation);
            if (!requests.Fits(next))
            {
                return await RejectAsync(context, requestId, ErrorCode.ContextTooLarge, "Недостаточно места для инструментов и результатов. Сократите запрос или сбросьте историю.", null, false, cancellationToken);
            }

            var completion = await llm.CompleteAsync(next, cancellationToken);
            if (completion is LlmCompletionResult.Failure failure)
            {
                await audit.RecordAsync(context, requestId, AuditAction.Reject, AuditStatus.Failed, null, cancellationToken);
                return new AgentCompletionResult.Failure(failure.Error);
            }

            if (completion is LlmCompletionResult.Success response)
            {
                return await FinishAsync(response.Response, context, requestId, usedCalls == 0, cancellationToken);
            }

            var calls = ((LlmCompletionResult.ToolCalls)completion).Calls;
            if (calls.Count == 0 || calls.Count > 2 - usedCalls || round == 2)
            {
                return await RejectAsync(context, requestId, ErrorCode.AgentToolLimit, "Достигнут лимит двух вызовов инструментов. Уточните запрос отдельным сообщением.", calls.FirstOrDefault(), true, cancellationToken);
            }

            foreach (var call in calls)
            {
                var definition = tools.FirstOrDefault(tool => tool.Name == call.Name);
                if (!usedIds.Add(call.Id) || definition is null || !schemas.IsValid(definition.InputSchema, call.Arguments))
                {
                    return await RejectAsync(context, requestId, ErrorCode.InvalidArguments, "Модель предложила неизвестный инструмент или некорректные параметры. Уточните запрос.", call, false, cancellationToken);
                }
            }

            if (calls.Any(call => call.Name == "add_reminder"))
            {
                if (calls.Count != 1)
                {
                    return await RejectAsync(context, requestId, ErrorCode.InvalidArguments, "Создание напоминания требует отдельного подтверждения.", calls[0], false, cancellationToken);
                }

                var prepared = await reminders.PrepareAsync(calls[0], context, timezone, cancellationToken);
                await audit.RecordAsync(context, requestId, AuditAction.Clarify,
                    prepared is AgentCompletionResult.Success ? AuditStatus.Pending : AuditStatus.Rejected, calls[0], cancellationToken);
                return prepared;
            }

            continuation.Add(new LlmMessage(LlmMessageRole.Assistant, RequestChecks.LlmText("Запрошены инструменты."), calls));
            foreach (var call in calls)
            {
                usedCalls++;
                Result<McpToolOutput> result = await mcp.CallReadToolAsync(call, cancellationToken);
                if (result is Result<McpToolOutput>.Failure unavailable)
                {
                    await audit.RecordAsync(context, requestId, AuditAction.ToolCall, AuditStatus.Failed, call, cancellationToken);
                    return new AgentCompletionResult.Failure(unavailable.Error);
                }

                var output = ((Result<McpToolOutput>.Success)result).Value;
                var definition = tools.Single(tool => tool.Name == call.Name);
                if (!schemas.IsValid(definition.OutputSchema, output.Data) || output.IsError != IsError(output.Data))
                {
                    return await RejectAsync(context, requestId, ErrorCode.InvalidMcpResponse, "Инструмент вернул результат, не соответствующий схеме.", call, false, cancellationToken);
                }

                await audit.RecordAsync(context, requestId, AuditAction.ToolCall,
                    output.IsError ? AuditStatus.Failed : AuditStatus.Succeeded, call, cancellationToken);
                var data = JsonSerializer.Serialize(new
                {
                    source = call.Name,
                    trust = "untrusted_data",
                    is_error = output.IsError,
                    data = output.Data
                });
                continuation.Add(new LlmMessage(LlmMessageRole.Tool, RequestChecks.LlmText(data), toolCallId: call.Id));
            }
        }

        return new AgentCompletionResult.Failure(new AppError(ErrorCode.AgentToolLimit, "Лимит агентного цикла исчерпан."));
    }

    private bool IsError(JsonElement value)
    {
        return value.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "error";
    }

    private async Task<AgentCompletionResult> FinishAsync(LlmResponse response, AgentTurnContext? context, Guid request, bool withoutTools, CancellationToken cancellationToken)
    {
        Result<AgentFinalReply> parsed = replies.Parse(response.Text);
        if (parsed is Result<AgentFinalReply>.Failure failure)
        {
            await audit.RecordAsync(context, request, AuditAction.Reject, AuditStatus.Failed, null, cancellationToken);
            return new AgentCompletionResult.Failure(failure.Error);
        }

        var reply = ((Result<AgentFinalReply>.Success)parsed).Value;
        response = response with
        {
            Text = reply.Text
        };
        context = context is null ? null : context with
        {
            DecisionReason = reply.Reason
        };
        var clarification = reply.RequiresClarification;
        if (withoutTools || clarification)
        {
            await audit.RecordAsync(context, request, clarification ? AuditAction.Clarify : AuditAction.Respond, AuditStatus.Succeeded, null, cancellationToken);
        }

        return new AgentCompletionResult.Success(response);
    }

    private async Task<AgentCompletionResult> RejectAsync(AgentTurnContext? context, Guid request, ErrorCode code,
        string message, LlmToolCall? call, bool limit, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(context, request, AuditAction.Reject, limit ? AuditStatus.LimitExceeded : AuditStatus.Rejected, call, cancellationToken);
        return new AgentCompletionResult.Failure(new AppError(code, message));
    }
}
