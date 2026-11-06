using ItmoBot.Configuration.Models;
using ItmoBot.Validation;

namespace ItmoBot.Tests.Support;

internal sealed class McpTestSettings
{
    public McpSettings Create(string? serverPath = null, string? scheduleFile = null, string timezone = "Europe/Moscow")
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = directory.Parent!.Name;
        while (!File.Exists(Path.Combine(directory.FullName, "ItmoBot.sln")))
        {
            directory = directory.Parent ?? throw new DirectoryNotFoundException("Не найден корень решения.");
        }

        string path = ConfigurationChecks.McpServerPath(serverPath
            ?? Path.Combine(directory.FullName, "mcp", "bin", configuration, "net10.0", "ItmoBot.Mcp.dll"));
        return new McpSettings(path, ConfigurationChecks.McpTimeout(15), new ScheduleSettings(
            ConfigurationChecks.ScheduleSourcePath(scheduleFile ?? Path.Combine(Path.GetDirectoryName(path)!, "Data", "schedule.json")), ConfigurationChecks.IanaTimeZone(timezone)),
            new WeatherHttpSettings(ConfigurationChecks.WeatherHttpTimeout(5), ConfigurationChecks.WeatherHttpAttempts(2)));
    }
}
