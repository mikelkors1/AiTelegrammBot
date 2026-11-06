namespace ItmoBot.Configuration.Models;

public sealed class McpSettings
{
    public McpSettings(string serverPath, TimeSpan timeout, ScheduleSettings schedule, WeatherHttpSettings weatherHttp)
    {
        ArgumentNullException.ThrowIfNull(serverPath);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(weatherHttp);
        ServerPath = serverPath;
        Timeout = timeout;
        Schedule = schedule;
        WeatherHttp = weatherHttp;
    }

    public string ServerPath
    {
        get;
    }

    public TimeSpan Timeout
    {
        get;
    }

    public ScheduleSettings Schedule
    {
        get;
    }

    public WeatherHttpSettings WeatherHttp
    {
        get;
    }
}
