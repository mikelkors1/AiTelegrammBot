using System.Globalization;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Mcp;
using ItmoBot.Validation;

namespace ItmoBot.Application.Commands;

public sealed class AgentCommandReplyFormatter(TimeProvider clock)
{
    public string Tools(McpCatalog catalog)
    {
        var lines = new List<string> { "Инструменты MCP:" };
        foreach (var tool in catalog.Tools)
        {
            var description = tool.Name switch
            {
                "get_schedule" => "чтение расписания на дату",
                "get_weather" => "чтение текущей погоды",
                "add_reminder" => "изменение: создание напоминания после вашего подтверждения",
                _ => "недоступен для вызова политикой приложения",
            };
            lines.Add($"• {tool.Name} — {description}");
        }

        if (catalog.Tools.Count == 0)
        {
            lines.Add("Сервер не объявил инструментов.");
        }

        if (catalog.Resources.Any(resource => resource.Uri == "schedule://current-week"))
        {
            lines.Add("Ресурс только для чтения: schedule://current-week — текущая неделя.");
        }

        return RequestChecks.LlmText(string.Join('\n', lines));
    }

    public string TimeZone(UserTimeZone? timezone)
    {
        if (timezone is null)
        {
            return RequestChecks.LlmText("Часовой пояс не задан. Укажите /timezone Europe/Moscow (или другую зону IANA).");
        }

        return RequestChecks.LlmText($"Часовой пояс: {timezone.Value.Id}\nСейчас: {timezone.ConvertInstant(clock.GetUtcNow()).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}\nПри смене зоны ожидающие подтверждения отменяются.");
    }

    public string Why(AgentAuditEvent entry, UserTimeZone? timezone)
    {
        var occurred = timezone?.ConvertInstant(entry.OccurredAt) ?? entry.OccurredAt.ToUniversalTime();
        var action = entry.Action switch
        {
            AuditAction.Respond => "обычный ответ",
            AuditAction.Clarify => "уточнение или ожидание подтверждения",
            AuditAction.ToolCall => "вызов инструмента",
            _ => "действие отклонено",
        };
        var status = entry.Status switch
        {
            AuditStatus.Pending => "ожидает подтверждения",
            AuditStatus.Succeeded => "успешно",
            AuditStatus.Cancelled => "отменено",
            AuditStatus.LimitExceeded => "превышен лимит",
            AuditStatus.Rejected => "отклонено",
            _ => "ошибка",
        };
        var lines = new List<string>
        {
            $"Время: {occurred.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)} ({timezone?.Value.Id ?? "UTC"})",
            $"Действие: {action}",
            $"Статус: {status}",
            $"Причина: {entry.Reason}",
        };
        if (entry.Tool is { } tool)
        {
            lines.Add($"Инструмент: {tool}");
        }

        if (entry.Arguments.City is { } city)
        {
            lines.Add($"Город: {city}");
        }

        if (entry.Arguments.ScheduleDate is { } date)
        {
            lines.Add($"Дата: {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        if (entry.Arguments.ReminderTextLength is { } length)
        {
            lines.Add($"Длина текста напоминания: {length.ToString(CultureInfo.InvariantCulture)}");
        }

        if (entry.Arguments.RemindAt is { } moment)
        {
            var zone = entry.Arguments.TimeZone ?? timezone;
            lines.Add($"Время напоминания: {(zone?.ConvertInstant(moment) ?? moment).ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)} ({zone?.Value.Id ?? "явное смещение"})");
        }

        if (entry.Duration is { } duration)
        {
            lines.Add($"Длительность: {duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} мс");
        }

        return RequestChecks.LlmText(string.Join('\n', lines));
    }
}
