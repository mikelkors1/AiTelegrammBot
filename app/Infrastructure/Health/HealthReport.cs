namespace ItmoBot.Infrastructure.Health;

public sealed record class HealthReport(bool DatabaseAvailable, bool PollingRunning, bool Initialized)
{
    public bool IsReady
    {
        get
        {
            return DatabaseAvailable && PollingRunning && Initialized;
        }
    }
}
