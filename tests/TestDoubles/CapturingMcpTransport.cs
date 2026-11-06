using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class CapturingMcpTransport(IClientTransport inner) : IClientTransport
{
    public ITransport? Connection
    {
        get; private set;
    }

    public string Name
    {
        get
        {
            return inner.Name;
        }
    }

    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        Connection = await inner.ConnectAsync(cancellationToken);
        return Connection;
    }
}
