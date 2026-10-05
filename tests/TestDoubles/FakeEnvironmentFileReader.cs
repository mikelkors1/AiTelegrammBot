using ItmoBot.Configuration.Contracts;
using ItmoBot.Configuration.ResultTypes;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeEnvironmentFileReader(EnvironmentReadResult result) : IEnvironmentFileReader
{
    public int ReadCalls
    {
        get; private set;
    }

    public EnvironmentReadResult Read(string path)
    {
        ReadCalls++;
        return result;
    }
}
