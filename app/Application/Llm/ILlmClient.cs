namespace ItmoBot.Application.Llm;

public interface ILlmClient : IDisposable
{
    Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}
