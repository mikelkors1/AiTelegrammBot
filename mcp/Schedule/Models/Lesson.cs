namespace ItmoBot.Mcp.Schedule.Models;

public sealed record class Lesson
{
    public Lesson(TimeOnly start, TimeOnly end, string title, string location)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(location);
        if (end <= start)
        {
            throw new ArgumentException("Окончание занятия должно быть позже начала.");
        }

        Start = start;
        End = end;
        Title = title;
        Location = location;
    }

    public TimeOnly Start
    {
        get;
    }
    public TimeOnly End
    {
        get;
    }
    public string Title
    {
        get;
    }
    public string Location
    {
        get;
    }
}
