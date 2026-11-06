namespace ItmoBot.Tests.Support;

internal sealed class TemporaryScheduleFile : IDisposable
{
    private readonly DirectoryInfo directory;

    public TemporaryScheduleFile(string content)
    {
        directory = Directory.CreateTempSubdirectory("itmo-schedule-");
        Path = System.IO.Path.Combine(directory.FullName, "schedule.json");
        File.WriteAllText(Path, content);
    }

    public string Path
    {
        get;
    }

    public void Dispose()
    {
        directory.Delete(recursive: true);
    }
}
