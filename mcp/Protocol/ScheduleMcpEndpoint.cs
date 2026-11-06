using System.Globalization;
using System.Text.Json;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Mcp.Schedule.Models;
using ItmoBot.Mcp.Schedule.ResultTypes;
using ItmoBot.Mcp.Schedule;
using ItmoBot.Mcp.Validation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;

namespace ItmoBot.Mcp.Protocol;

public sealed class ScheduleMcpEndpoint(ScheduleService schedule, UserTimeZone timezone)
{
    public ValueTask<ListToolsResult> ListToolsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ListToolsResult { Tools = [GetTool()] });
    }

    public ValueTask<ListResourcesResult> ListResourcesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ListResourcesResult { Resources = [GetResource()] });
    }

    public async ValueTask<CallToolResult> CallAsync(CallToolRequestParams? request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request?.Name != "get_schedule")
        {
            throw new McpProtocolException("Неизвестный инструмент.", McpErrorCode.InvalidParams);
        }

        var arguments = request.Arguments;
        if (arguments is null || arguments.Count != 1 || !arguments.TryGetValue("date", out var date)
            || date.ValueKind != JsonValueKind.String)
        {
            return InvalidArguments();
        }

        DateOnly parsed;
        try
        {
            parsed = ScheduleChecks.ScheduleDate(date.GetString()!);
        }
        catch (ArgumentException)
        {
            return InvalidArguments();
        }

        var result = await schedule.GetDayAsync(parsed, cancellationToken);
        return result is ScheduleResult<ScheduleDay>.Failure failure
            ? Failure(failure.Error)
            : Success(FormatDay(((ScheduleResult<ScheduleDay>.Success)result).Value));
    }

    public async ValueTask<ReadResourceResult> ReadAsync(ReadResourceRequestParams? request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request?.Uri != "schedule://current-week")
        {
            throw new McpProtocolException("Неизвестный ресурс.", McpErrorCode.InvalidParams);
        }

        var result = await schedule.GetCurrentWeekAsync(cancellationToken);
        if (result is ScheduleResult<ScheduleWeek>.Failure failure)
        {
            throw new McpProtocolException(failure.Error.Message, McpErrorCode.InternalError);
        }

        var value = FormatWeek(((ScheduleResult<ScheduleWeek>.Success)result).Value);
        return new ReadResourceResult
        {
            Contents = [new TextResourceContents { Uri = "schedule://current-week", MimeType = "application/json", Text = value.GetRawText() }],
        };
    }

    private CallToolResult InvalidArguments()
    {
        return Failure(new ScheduleError(ScheduleErrorCode.InvalidArguments,
            "Передайте только поле date со строкой существующей даты в формате YYYY-MM-DD."));
    }

    public static Tool GetTool()
    {
        return new Tool
        {
            Name = "get_schedule",
            Description = "Занятия на указанную календарную дату. date — существующая дата YYYY-MM-DD. Пустой список означает отсутствие занятий.",
            InputSchema = Schema("""
                {"type":"object","properties":{"date":{"type":"string","format":"date","pattern":"^[0-9]{4}-[0-9]{2}-[0-9]{2}$"}},"required":["date"],"additionalProperties":false}
                """),
            OutputSchema = Schema("""
                {
                  "type":"object",
                  "$defs":{"lesson":{"type":"object","properties":{
                    "start":{"type":"string","pattern":"^([01][0-9]|2[0-3]):[0-5][0-9]$"},
                    "end":{"type":"string","pattern":"^([01][0-9]|2[0-3]):[0-5][0-9]$"},
                    "title":{"type":"string","minLength":1,"maxLength":200},
                    "location":{"type":"string","minLength":1,"maxLength":200}},
                    "required":["start","end","title","location"],"additionalProperties":false}},
                  "oneOf":[
                    {"type":"object","properties":{
                      "date":{"type":"string","format":"date"},
                      "timezone":{"type":"string","minLength":1},
                      "lessons":{"type":"array","items":{"$ref":"#/$defs/lesson"}},
                      "source":{"const":"repository_schedule"}},
                      "required":["date","timezone","lessons","source"],"additionalProperties":false},
                    {"type":"object","properties":{
                      "status":{"const":"error"},
                      "error":{"type":"object","properties":{
                        "code":{"enum":["invalid_arguments","source_unavailable"]},
                        "message":{"type":"string","minLength":1},"retryable":{"type":"boolean"}},
                        "required":["code","message","retryable"],"additionalProperties":false}},
                      "required":["status","error"],"additionalProperties":false}
                  ]
                }
                """),
            Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, OpenWorldHint = false },
        };
    }

    public static Resource GetResource()
    {
        return new Resource
        {
            Uri = "schedule://current-week",
            Name = "Расписание текущей недели",
            Description = "Обезличенное расписание с понедельника по воскресенье в настроенной серверной IANA-зоне.",
            MimeType = "application/json",
        };
    }

    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private JsonElement FormatDay(ScheduleDay day)
    {
        return JsonSerializer.SerializeToElement(new
        {
            date = Date(day.Date),
            timezone = timezone.Value.Id,
            lessons = day.Lessons.Select(FormatLesson),
            source = "repository_schedule",
        });
    }

    private JsonElement FormatWeek(ScheduleWeek week)
    {
        return JsonSerializer.SerializeToElement(new
        {
            week_start = Date(week.Start),
            week_end = Date(week.End),
            timezone = timezone.Value.Id,
            source = "repository_schedule",
            days = week.Days.Select(day => new { date = Date(day.Date), lessons = day.Lessons.Select(FormatLesson) }),
        });
    }

    private CallToolResult Success(JsonElement value)
    {
        return new CallToolResult
        {
            StructuredContent = value,
            Content = [new TextContentBlock { Text = value.GetRawText() }],
            IsError = false,
        };
    }

    private CallToolResult Failure(ScheduleError error)
    {
        var value = JsonSerializer.SerializeToElement(new
        {
            status = "error",
            error = new
            {
                code = error.Code switch
                {
                    ScheduleErrorCode.InvalidArguments => "invalid_arguments",
                    ScheduleErrorCode.SourceUnavailable => "source_unavailable",
                    _ => throw new ArgumentOutOfRangeException(nameof(error)),
                },
                message = error.Message,
                retryable = error.Retryable,
            },
        });
        return new CallToolResult
        {
            StructuredContent = value,
            Content = [new TextContentBlock { Text = value.GetRawText() }],
            IsError = true,
        };
    }

    private object FormatLesson(Lesson lesson)
    {
        return new
        {
            start = lesson.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
            end = lesson.End.ToString("HH:mm", CultureInfo.InvariantCulture),
            title = lesson.Title,
            location = lesson.Location,
        };
    }

    private string Date(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
