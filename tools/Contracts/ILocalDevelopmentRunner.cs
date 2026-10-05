using ItmoBot.Tools.ResultTypes;

namespace ItmoBot.Tools.Contracts;

public interface ILocalDevelopmentRunner
{
    Task<LocalDevelopmentResult> RunAsync(string[] args);
}
