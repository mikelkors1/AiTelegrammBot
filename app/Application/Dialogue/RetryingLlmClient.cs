using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Dialogue;

public sealed class RetryingLlmClient(
    ILlmClient inner,
    LlmRetryWindow window,
    TimeProvider time,
    ILogger logger) : ILlmClient
{
    private int disposed;

    public async Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource(window.Value, time);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var started = time.GetTimestamp();
        try
        {
            return await TryUntilDeadlineAsync(request, started, stopping.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return TimedOut();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            inner.Dispose();
        }
    }

    private async Task<LlmCompletionResult> TryUntilDeadlineAsync(LlmRequest request, long started, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (Remaining(started) > TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            var result = await inner.CompleteAsync(request, cancellationToken).WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (Remaining(started) <= TimeSpan.Zero)
            {
                return TimedOut();
            }

            if (result is LlmCompletionResult.Success)
            {
                return result;
            }

            var failure = (LlmCompletionResult.Failure)result;
            logger.LogWarning("LLM попытка {Attempt} завершилась ошибкой {Code}; ожидание ответа продолжается.", attempt, failure.Error.Code);
            var delay = Delay(attempt, failure.RetryAfter);
            var remaining = Remaining(started);
            if (remaining <= TimeSpan.Zero)
            {
                return TimedOut();
            }

            await Task.Delay(delay < remaining ? delay : remaining, time, cancellationToken);
        }

        return TimedOut();
    }

    private TimeSpan Remaining(long started)
    {
        return window.Value - time.GetElapsedTime(started);
    }

    private TimeSpan Delay(int attempt, TimeSpan? retryAfter)
    {
        var backoff = TimeSpan.FromSeconds(attempt switch
        {
            1 => 5,
            2 => 10,
            3 => 20,
            _ => 30,
        });
        return retryAfter is { } serverDelay && serverDelay > backoff ? serverDelay : backoff;
    }

    private LlmCompletionResult TimedOut()
    {
        logger.LogWarning("Общее время ожидания ответа LLM истекло: {Seconds} секунд.", window.Value.TotalSeconds);
        return new LlmCompletionResult.Failure(new AppError(ErrorCode.LlmRetryTimeout,
            "Модель не ответила за отведённое время. Попробуйте позже.", IsRetryable: true));
    }
}
