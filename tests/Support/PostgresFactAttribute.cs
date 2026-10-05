using Xunit;

namespace ItmoBot.Tests.Support;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_INTEGRATION") != "1")
        {
            Skip = "Для отдельной тестовой PostgreSQL установите RUN_INTEGRATION=1. См. README.";
        }
    }
}
