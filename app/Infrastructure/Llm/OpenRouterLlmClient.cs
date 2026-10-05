using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Llm.Contracts;

namespace ItmoBot.Infrastructure.Llm;

public sealed class OpenRouterLlmClient(
    LlmSettings settings,
    HttpClient http,
    ILlmRequestWriter requestWriter,
    ILlmResponseParser responseParser,
    ILlmErrorMapper errorMapper,
    ILogger logger) : ILlmClient
{
    public async Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid();
        var elapsed = Stopwatch.StartNew();
        logger.LogInformation("LLM вызов {RequestId} начат: модель {Model}.", requestId, settings.Model.Value);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout.Value);
        try
        {
            var result = await SendAsync(request, timeout.Token);
            LogResult(result, requestId, elapsed.Elapsed);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("LLM вызов {RequestId} отменён, длительность {DurationMs} мс.", requestId, elapsed.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or TimeoutException or JsonException or IOException)
        {
            var result = new LlmCompletionResult.Failure(errorMapper.MapException(error));
            // Exception messages can contain request data; log only the safe classification.
            LogResult(result, requestId, elapsed.Elapsed);
            return result;
        }
    }

    public void Dispose()
    {
        http.Dispose();
    }

    private async Task<LlmCompletionResult> SendAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.ApiAddress.Value, "chat/completions"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Value);
        message.Content = requestWriter.CreateContent(request, settings);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new LlmCompletionResult.Failure(errorMapper.MapStatus((int)response.StatusCode), ReadRetryAfter(response));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var parsed = responseParser.Parse(document.RootElement);
        if (parsed is LlmCompletionResult.Failure failure)
        {
            return failure with
            {
                RetryAfter = ReadRetryAfter(response)
            };
        }

        return parsed;
    }

    private TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var delay = header?.Delta;
        if (delay is null && header?.Date is { } date)
        {
            delay = date - DateTimeOffset.UtcNow;
        }

        return delay is { } value && value >= TimeSpan.Zero ? delay : null;
    }

    private void LogResult(LlmCompletionResult result, Guid requestId, TimeSpan duration)
    {
        if (result is LlmCompletionResult.Failure failure)
        {
            logger.LogWarning("LLM вызов {RequestId} завершён ошибкой {Code}: модель {Model}, длительность {DurationMs} мс.",
                requestId, failure.Error.Code, settings.Model.Value, duration.TotalMilliseconds);
        }
        else
        {
            logger.LogInformation("LLM вызов {RequestId} завершён успешно: модель {Model}, длительность {DurationMs} мс.",
                requestId, settings.Model.Value, duration.TotalMilliseconds);
        }
    }
}
