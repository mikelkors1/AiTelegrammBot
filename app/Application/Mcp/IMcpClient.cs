using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Mcp;

public interface IMcpClient
{
    Task<Result<IMcpSession>> ConnectAsync(CancellationToken cancellationToken);
}
