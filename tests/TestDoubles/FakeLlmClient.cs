using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeLlmClient : ILlmClient
{
    public List<LlmRequest> Requests { get; } = [];
    public LlmText Text { get; set; } = new("Ответ модели");
    public AppError? Error
    {
        get; set;
    }
    public int DisposeCalls
    {
        get; private set;
    }
    public Func<LlmRequest, CancellationToken, Task<LlmCompletionResult>>? OnComplete
    {
        get; set;
    }

    public async Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        if (OnComplete is not null)
        {
            return await OnComplete(request, cancellationToken);
        }

        if (Error is not null)
        {
            return new LlmCompletionResult.Failure(Error);
        }

        return new LlmCompletionResult.Success(new LlmResponse(Text, new ModelName("qwen/qwen3.8-27b:free"), null, null));
    }

    public void Dispose()
    {
        DisposeCalls++;
    }
}
