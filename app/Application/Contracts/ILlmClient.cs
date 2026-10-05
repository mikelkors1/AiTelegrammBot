using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Application.Contracts;

public interface ILlmClient : IDisposable
{
    Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}
