using ItmoBot.Configuration.Contracts;
using ItmoBot.Shared.Results;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeEnvironmentFileReader(Result<IReadOnlyDictionary<string, string>> result) : IEnvironmentFileReader
{
    public int ReadCalls
    {
        get; private set;
    }

    public Result<IReadOnlyDictionary<string, string>> Read(string path)
    {
        ReadCalls++;
        return result;
    }
}
