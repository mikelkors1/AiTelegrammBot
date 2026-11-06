using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Agent.ResultTypes;

public abstract record class AgentStorageResult<T>
{
    private AgentStorageResult()
    {
    }

    public sealed record class Success(T Value) : AgentStorageResult<T>;

    public sealed record class NotFound : AgentStorageResult<T>;

    public sealed record class Failure(AppError Error) : AgentStorageResult<T>;
}
