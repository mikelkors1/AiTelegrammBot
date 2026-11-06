namespace ItmoBot.Tools;

public sealed class EvaluationTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}
