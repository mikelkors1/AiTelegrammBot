using System.Globalization;
using System.Text.Json;
using ItmoBot.Agent.Security;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Mcp.Reminders;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Validation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;

namespace ItmoBot.Mcp.Protocol;

public sealed class ReminderMcpEndpoint(ReminderToolService service, ActionContextMetadata metadata)
{
    public async ValueTask<CallToolResult> CallAsync(CallToolRequestParams? request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request?.Name != "add_reminder")
        {
            throw new McpProtocolException("Неизвестный инструмент.", McpErrorCode.InvalidParams);
        }

        var arguments = request.Arguments;
        if (arguments is null || arguments.Count != 2 || !arguments.TryGetValue("text", out var text)
            || !arguments.TryGetValue("remind_at", out var time) || text.ValueKind != JsonValueKind.String || time.ValueKind != JsonValueKind.String)
        {
            return InvalidArguments();
        }

        string parsedText;
        try
        {
            parsedText = AgentChecks.ReminderText(text.GetString()!);
        }
        catch (ArgumentException)
        {
            return InvalidArguments();
        }

        var timeText = time.GetString()!;
        if (timeText.EndsWith('Z'))
        {
            timeText = timeText[..^1] + "+00:00";
        }

        if (!DateTimeOffset.TryParseExact(timeText, ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
        {
            return InvalidArguments();
        }

        return Format(await service.CreateReminderAsync(parsedText, moment, metadata.Decode(request.Meta), cancellationToken));
    }

    private CallToolResult InvalidArguments()
    {
        return Format(new AgentStorageResult<ReminderCreation>.Failure(new AppError(ErrorCode.InvalidArguments,
            "Передайте только text (1–500 символов) и remind_at (ISO 8601 с явным смещением или Z).")));
    }

    public static Tool GetTool()
    {
        return new Tool
        {
            Name = "add_reminder",
            Description = """
                Двухэтапное создание напоминания. Когда известны текст, будущее время и часовой пояс,
                модель сразу предлагает вызов add_reminder с text и remind_at — это предложение параметров,
                для него предварительное согласие текстом не нужно. Telegram-приложение перехватывает
                предложение, сохраняет подготовленное действие и показывает кнопки «Подтвердить»/«Отменить».
                Сам MCP-сервер сохраняет напоминание только после явного подтверждения кнопкой;
                без доверенного контекста и сохранённого подтверждения выполнение запрещено.
                text — текст 1–500 символов; remind_at — будущее ISO 8601 время с явным смещением или Z.
                Владелец и подтверждение передаются доверенным клиентом вне аргументов модели.
                Повтор подтверждённого действия возвращает прежний результат.
                """,
            InputSchema = Schema("""
                {"type":"object","properties":{
                  "text":{"type":"string","minLength":1,"maxLength":500},
                  "remind_at":{"type":"string","format":"date-time"}},
                 "required":["text","remind_at"],"additionalProperties":false}
                """),
            OutputSchema = Schema("""
                {"type":"object","oneOf":[
                  {"type":"object","properties":{
                    "reminder_id":{"type":"string","format":"uuid"},
                    "remind_at":{"type":"string","format":"date-time"},
                    "timezone":{"type":"string","minLength":1},"created":{"type":"boolean"}},
                    "required":["reminder_id","remind_at","timezone","created"],"additionalProperties":false},
                  {"type":"object","properties":{
                    "status":{"const":"error"},"error":{"type":"object","properties":{
                      "code":{"enum":["invalid_arguments","unauthorized","action_not_confirmed","action_expired","action_cancelled","action_mismatch","storage_unavailable"]},
                      "message":{"type":"string","minLength":1},"retryable":{"type":"boolean"}},
                      "required":["code","message","retryable"],"additionalProperties":false}},
                    "required":["status","error"],"additionalProperties":false}]}
                """),
            Annotations = new ToolAnnotations { ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false },
        };
    }

    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private CallToolResult Format(AgentStorageResult<ReminderCreation> result)
    {
        var value = result is AgentStorageResult<ReminderCreation>.Success success
            ? JsonSerializer.SerializeToElement(new
            {
                reminder_id = success.Value.Reminder.Id,
                remind_at = success.Value.Reminder.Payload.Schedule.Moment.ToString("O", CultureInfo.InvariantCulture),
                timezone = success.Value.Reminder.Payload.Schedule.TimeZone.Value.Id,
                created = success.Value.Created,
            })
            : FormatFailure(result);
        return new CallToolResult
        {
            StructuredContent = value,
            Content = [new TextContentBlock { Text = value.GetRawText() }],
            IsError = result is not AgentStorageResult<ReminderCreation>.Success,
        };
    }

    private JsonElement FormatFailure(AgentStorageResult<ReminderCreation> result)
    {
        var error = result is AgentStorageResult<ReminderCreation>.Failure failure
            ? failure.Error : new AppError(ErrorCode.Unauthorized, "Нет полномочий для выполнения действия.");
        var code = error.Code switch
        {
            ErrorCode.InvalidArguments or ErrorCode.ReminderTimeInPast => "invalid_arguments",
            ErrorCode.Unauthorized => "unauthorized",
            ErrorCode.ActionNotConfirmed => "action_not_confirmed",
            ErrorCode.ActionExpired => "action_expired",
            ErrorCode.ActionCancelled => "action_cancelled",
            ErrorCode.ActionMismatch => "action_mismatch",
            _ => "storage_unavailable",
        };
        return JsonSerializer.SerializeToElement(new
        {
            status = "error",
            error = new
            {
                code,
                message = code == "storage_unavailable" ? "Хранилище напоминаний недоступно." : error.Message,
                retryable = error.IsRetryable,
            },
        });
    }
}
