using System.Globalization;

namespace ItmoBot.Mcp.Validation;

public static class ScheduleChecks
{
    public static string ScheduleSourcePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            throw new ArgumentException("SCHEDULE_FILE: укажите путь к JSON-файлу расписания.");
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("SCHEDULE_FILE: указан некорректный путь к JSON-файлу расписания.");
        }
    }

    public static TimeOnly LessonTime(string value)
    {
        if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            throw new ArgumentException("Время занятия должно иметь формат HH:mm.");
        }

        return time;
    }

    public static DateOnly ScheduleDate(string value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new ArgumentException("Дата должна существовать и иметь формат YYYY-MM-DD.");
        }

        return date;
    }

    public static DateOnly ScheduleDate(DateOnly value)
    {
        return value;
    }

    public static string LessonText(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 200 || value.Any(char.IsControl))
        {
            throw new ArgumentException("Название занятия и место должны содержать от 1 до 200 символов без управляющих символов.");
        }

        return value.Trim();
    }

}
