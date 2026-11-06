using System.Text.Json;
using ItmoBot.Mcp.Schedule.Contracts;
using ItmoBot.Mcp.Schedule.Models;
using ItmoBot.Mcp.Schedule.ResultTypes;
using ItmoBot.Mcp.Validation;

namespace ItmoBot.Mcp.Infrastructure.Schedule;

public sealed class JsonScheduleSource(string path) : IScheduleSource
{
    public async Task<ScheduleResult<ScheduleSnapshot>> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = File.OpenRead(path);
            if (stream.Length > 1_048_576)
            {
                throw new JsonException("Файл расписания слишком большой.");
            }

            using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
            return new ScheduleResult<ScheduleSnapshot>.Success(Parse(document.RootElement));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new ScheduleResult<ScheduleSnapshot>.Failure(new ScheduleError(ScheduleErrorCode.SourceUnavailable,
                "Не удалось прочитать источник расписания. Проверьте его наличие и формат."));
        }
    }

    private ScheduleSnapshot Parse(JsonElement root)
    {
        ValidateFields(root, "days");
        var days = root.GetProperty("days");
        if (days.ValueKind != JsonValueKind.Array || days.GetArrayLength() == 0)
        {
            throw new JsonException("Источник должен содержать дни расписания.");
        }

        var result = new Dictionary<DateOnly, IReadOnlyList<Lesson>>();
        foreach (var day in days.EnumerateArray())
        {
            ValidateFields(day, "date", "lessons");
            DateOnly date = ScheduleChecks.ScheduleDate(ReadString(day, "date"));
            var lessons = day.GetProperty("lessons");
            if (lessons.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Занятия должны быть массивом.");
            }

            if (!result.TryAdd(date, Array.AsReadOnly(lessons.EnumerateArray().Select(ParseLesson).ToArray())))
            {
                throw new JsonException("Дата повторяется в источнике расписания.");
            }
        }

        return new ScheduleSnapshot(result);
    }

    private Lesson ParseLesson(JsonElement lesson)
    {
        ValidateFields(lesson, "start", "end", "title", "location");
        return new Lesson(ScheduleChecks.LessonTime(ReadString(lesson, "start")), ScheduleChecks.LessonTime(ReadString(lesson, "end")),
            ScheduleChecks.LessonText(ReadString(lesson, "title")), ScheduleChecks.LessonText(ReadString(lesson, "location")));
    }

    private string ReadString(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Ожидалось строковое поле расписания.");
        }

        return value.GetString()!;
    }

    private void ValidateFields(JsonElement element, params string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Ожидался объект расписания.");
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!required.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name))
            {
                throw new JsonException("Неизвестное или повторяющееся поле расписания.");
            }
        }

        if (found.Count != required.Length)
        {
            throw new JsonException("Отсутствует обязательное поле расписания.");
        }
    }
}
