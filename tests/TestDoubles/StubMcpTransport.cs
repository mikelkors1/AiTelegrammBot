using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class StubMcpTransport(Func<CancellationToken, Task<ITransport>> connect) : IClientTransport
{
    public string Name
    {
        get
        {
            return "TestMcpTransport";
        }
    }

    public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        return connect(cancellationToken);
    }
}
