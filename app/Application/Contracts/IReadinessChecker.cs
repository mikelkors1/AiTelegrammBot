using ItmoBot.Application.ResultTypes;
using ItmoBot.Shared.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IReadinessChecker : IDisposable
{
    Task<ReadinessCheckResult> CheckAsync(PortNumber port, CancellationToken cancellationToken = default);
}
