using ItmoBot.Application.Mcp;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeMcpClient : IMcpClient
{
    public FakeMcpClient()
    {
        Result = new Result<IMcpSession>.Failure(new AppError(ErrorCode.McpUnavailable, "Тестовый MCP недоступен."));
    }

    public Result<IMcpSession> Result
    {
        get; set;
    }

    public Func<CancellationToken, Task<Result<IMcpSession>>>? OnConnect
    {
        get; set;
    }

    public int ConnectCalls
    {
        get; private set;
    }

    public Task<Result<IMcpSession>> ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectCalls++;
        return OnConnect is null ? Task.FromResult(Result) : OnConnect(cancellationToken);
    }
}
