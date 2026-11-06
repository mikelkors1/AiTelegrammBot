using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.Json;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Mcp;
using ItmoBot.Configuration.Models;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Validation;
using ItmoBot.Validation;

namespace ItmoBot.Infrastructure.Llm;

public sealed class OpenRouterLlmClient(LlmSettings settings, HttpClient http, ILogger logger, AgentRequestBuilder requestBudget) : ILlmClient
{
    public async Task<LlmCompletionResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid();
        var elapsed = Stopwatch.StartNew();
        logger.LogInformation("LLM вызов {RequestId} начат: модель {Model}.", requestId, settings.Model);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);
        try
        {
            request = PrepareNativeDecision(request, settings);
            request = requestBudget.TrimHistoryToFit(request);
            if (!requestBudget.Fits(request))
            {
                return new LlmCompletionResult.Failure(new AppError(ErrorCode.ContextTooLarge, "Недостаточно места для выбора действия."));
            }

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
            var result = new LlmCompletionResult.Failure(MapException(error));
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
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.ApiAddress, "chat/completions"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Value);
        message.Content = CreateContent(request, settings);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 429)
            {
                return await ReadRateLimitAsync(response, cancellationToken);
            }

            return new LlmCompletionResult.Failure(MapStatus((int)response.StatusCode), ReadRetryAfter(response));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var parsed = ParseResponse(document.RootElement);
        if (parsed is LlmCompletionResult.Failure failure)
        {
            return failure with
            {
                RetryAfter = ReadRetryAfter(response)
            };
        }

        if (UsesNativeDecision(request, settings))
        {
            if (parsed is LlmCompletionResult.Success)
            {
                return new LlmCompletionResult.Failure(new AppError(ErrorCode.InvalidLlmResponse,
                    "Модель вернула текст вместо обязательного выбора действия."));
            }

            var calls = ((LlmCompletionResult.ToolCalls)parsed).Calls;
            if (calls.Any(call => call.Name == "submit_final_reply"))
            {
                if (calls.Count != 1 || !new ToolSchemaValidator().IsValid(request.FinalResponseSchema!.Value, calls[0].Arguments))
                {
                    return new LlmCompletionResult.Failure(new AppError(ErrorCode.InvalidLlmResponse, "Некорректное завершение ответа модели."));
                }

                return new LlmCompletionResult.Success(new LlmResponse(calls[0].Arguments.GetRawText(), settings.Model, null, null));
            }
        }

        return parsed;
    }

    private async Task<LlmCompletionResult.Failure> ReadRateLimitAsync(HttpResponseMessage response, CancellationToken token)
    {
        var fallback = new LlmCompletionResult.Failure(MapStatus(429), ReadRetryAfter(response));
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[65537];
            var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken: token);
            if (length > 65536)
            {
                return fallback;
            }

            using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object || !error.TryGetProperty("metadata", out var metadata)
                || metadata.ValueKind != JsonValueKind.Object)
            {
                return fallback;
            }

            var daily = metadata.TryGetProperty("limit_source", out var source)
                && source.ValueKind == JsonValueKind.String && source.GetString() == "openrouter_free_tier_daily";
            DateTimeOffset? reset = null;
            if (metadata.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
            {
                var values = headers.EnumerateObject().ToDictionary(property => property.Name,
                    property => property.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                if (values.TryGetValue("X-RateLimit-Reset", out var resetValue)
                    && long.TryParse(resetValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var timestamp)
                    && timestamp is >= -62135596800000 and <= 253402300799999)
                {
                    reset = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
                }

                // Старый формат OpenRouter: отдельного limit_source нет, но есть заголовки суточной free-квоты.
                if (!metadata.TryGetProperty("limit_source", out _)
                    && (!metadata.TryGetProperty("provider_name", out var provider) || provider.ValueKind == JsonValueKind.Null)
                    && settings.Model.EndsWith(":free", StringComparison.Ordinal)
                    && values.TryGetValue("X-RateLimit-Limit", out var limit) && limit is "50" or "1000"
                    && values.TryGetValue("X-RateLimit-Remaining", out var remaining) && remaining == "0"
                    && reset is { } next && next.TimeOfDay == TimeSpan.Zero
                    && next > DateTimeOffset.UtcNow && next - DateTimeOffset.UtcNow <= TimeSpan.FromDays(1))
                {
                    daily = true;
                }
            }

            return daily
                ? new LlmCompletionResult.Failure(new AppError(ErrorCode.LlmDailyQuotaExceeded,
                    "Суточный лимит запросов OpenRouter исчерпан. Дождитесь обновления квоты."), QuotaResetsAt: reset)
                : fallback;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            return fallback;
        }
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
        if (result is LlmCompletionResult.Failure { Error.Code: ErrorCode.LlmDailyQuotaExceeded } quota)
        {
            if (quota.QuotaResetsAt is { } reset)
            {
                logger.LogWarning("Суточный лимит запросов OpenRouter исчерпан. Обновление квоты: {ResetUtc} UTC.",
                    reset.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                logger.LogWarning("Суточный лимит запросов OpenRouter исчерпан. Ожидается обновление квоты.");
            }
        }
        else if (result is LlmCompletionResult.Failure failure)
        {
            logger.LogWarning("LLM вызов {RequestId} завершён ошибкой {Code}: модель {Model}, длительность {DurationMs} мс.",
                requestId, failure.Error.Code, settings.Model, duration.TotalMilliseconds);
        }
        else
        {
            logger.LogInformation("LLM вызов {RequestId} завершён успешно: модель {Model}, длительность {DurationMs} мс.",
                requestId, settings.Model, duration.TotalMilliseconds);
        }
    }

    private static bool UsesNativeDecision(LlmRequest request, LlmSettings settings)
    {
        return settings.StructuredOutputMode == LlmStructuredOutputMode.ToolCompatibleJson
            && request.Tools.Count > 0 && request.FinalResponseSchema is not null;
    }

    private static LlmRequest PrepareNativeDecision(LlmRequest request, LlmSettings settings)
    {
        if (!UsesNativeDecision(request, settings) || request.Tools.Any(tool => tool.Name == "submit_final_reply"))
        {
            return request;
        }

        var final = new McpToolDefinition("submit_final_reply",
            "Завершить обычный ответ или задать уточнение. Не использовать вместо get_weather/get_schedule/add_reminder, когда нужны внешние данные или подготовка напоминания. После результата инструмента сформулировать итог здесь. В text включить полный ответ и весь запрошенный код.",
            request.FinalResponseSchema!.Value, request.FinalResponseSchema.Value, true);
        var instruction = new LlmMessage(LlmMessageRole.System,
            "Сейчас обязателен нативный вызов функции. Для внешних данных выбирай соответствующий инструмент; для обычного ответа или уточнения — submit_final_reply. Не пиши вызовы функций обычным текстом. submit_final_reply не получает внешние данные и не создаёт напоминаний.");
        return new LlmRequest(request.Messages.Append(instruction), request.Temperature,
            request.Tools.Append(final).ToArray(), request.FinalResponseSchema,
            request.MandatoryMessageCount, request.HistoryMessageCount);
    }

    public static HttpContent CreateContent(LlmRequest request, LlmSettings settings)
    {
        request = PrepareNativeDecision(request, settings);
        return JsonContent.Create(new
        {
            model = settings.Model,
            messages = request.Messages.Select(message => new
            {
                role = message.Role.ToString().ToLowerInvariant(),
                content = message.ToolCalls.Count > 0 ? null : message.Content,
                tool_call_id = message.ToolCallId,
                tool_calls = message.ToolCalls.Count == 0 ? null : message.ToolCalls.Select(call => new
                {
                    id = call.Id,
                    type = "function",
                    function = new
                    {
                        name = call.Name,
                        arguments = call.Arguments.GetRawText()
                    },
                }),
            }),
            tools = request.Tools.Count == 0 ? null : request.Tools.Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = tool.InputSchema
                },
            }),
            response_format = ResponseFormat(request, settings),
            tool_choice = request.Tools.Count == 0 ? null : UsesNativeDecision(request, settings) ? "required" : "auto",
            temperature = request.Temperature,
            max_tokens = settings.MaxOutputTokens,
            stream = false,
            reasoning = new
            {
                enabled = false
            },
            provider = new
            {
                require_parameters = true,
                allow_fallbacks = false
            },
        }, options: new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
    }

    private static object? ResponseFormat(LlmRequest request, LlmSettings settings)
    {
        if (request.FinalResponseSchema is null
            || (settings.StructuredOutputMode == LlmStructuredOutputMode.ToolCompatibleJson && request.Tools.Count > 0))
        {
            return null;
        }

        if (settings.StructuredOutputMode is LlmStructuredOutputMode.JsonObject or LlmStructuredOutputMode.ToolCompatibleJson)
        {
            return new
            {
                type = "json_object"
            };
        }

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "agent_reply",
                strict = true,
                schema = request.FinalResponseSchema
            },
        };
    }


    public static LlmCompletionResult ParseResponse(JsonElement root)
    {
        try
        {
            return ParseCore(root);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException
            or OverflowException or ArgumentException or KeyNotFoundException)
        {
            return InvalidResponse();
        }
    }

    private static LlmCompletionResult ParseCore(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return InvalidResponse();
        }

        if (root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code)
                && code.TryGetInt32(out var status))
            {
                return new LlmCompletionResult.Failure(MapStatus(status));
            }

            return InvalidResponse();
        }

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1)
        {
            return InvalidResponse();
        }

        return ParseChoice(root, choices[0]);
    }

    private static LlmCompletionResult ParseChoice(JsonElement root, JsonElement choice)
    {
        if (!choice.TryGetProperty("finish_reason", out var finish))
        {
            return InvalidResponse();
        }

        if (finish.GetString() == "length")
        {
            return new LlmCompletionResult.Failure(new AppError(ErrorCode.IncompleteLlmResponse,
                "Ответ модели оборвался из-за ограничения длины. Сократите запрос или увеличьте лимит ответа."));
        }

        if (finish.GetString() == "content_filter")
        {
            return new LlmCompletionResult.Failure(MapStatus(403));
        }

        if (finish.GetString() == "tool_calls")
        {
            return ParseToolCalls(root, choice.GetProperty("message"));
        }

        if (finish.GetString() != "stop" || !choice.TryGetProperty("message", out var message))
        {
            return InvalidResponse();
        }

        if (message.TryGetProperty("tool_calls", out var unexpected) && unexpected.ValueKind != JsonValueKind.Null
            && (unexpected.ValueKind != JsonValueKind.Array || unexpected.GetArrayLength() > 0))
        {
            return InvalidResponse();
        }

        return ParseMessage(root, message);
    }

    private static LlmCompletionResult ParseMessage(JsonElement root, JsonElement message)
    {
        if (message.TryGetProperty("role", out var role) && role.GetString() != "assistant")
        {
            return InvalidResponse();
        }

        if (!message.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null)
        {
            return EmptyResponse();
        }

        var text = content.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return EmptyResponse();
        }

        if (!root.TryGetProperty("model", out var model))
        {
            return InvalidResponse();
        }

        string? provider = root.TryGetProperty("provider", out var providerValue) && providerValue.ValueKind != JsonValueKind.Null
            ? RequestChecks.LlmProviderName(providerValue.GetString()!) : null;
        return new LlmCompletionResult.Success(new LlmResponse(
            RequestChecks.LlmText(text), RequestChecks.ModelName(model.GetString()!), ParseUsage(root), provider));
    }

    private static LlmCompletionResult ParseToolCalls(JsonElement root, JsonElement message)
    {
        _ = RequestChecks.ModelName(root.GetProperty("model").GetString()!);
        if (message.GetProperty("role").GetString() != "assistant"
            || !message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array
            || calls.GetArrayLength() is < 1 or > 8)
        {
            return InvalidResponse();
        }

        var parsed = new List<LlmToolCall>();
        foreach (var call in calls.EnumerateArray())
        {
            if (call.GetProperty("type").GetString() != "function")
            {
                return InvalidResponse();
            }

            var function = call.GetProperty("function");
            var argumentsText = function.GetProperty("arguments").GetString()!;
            if (argumentsText is null || argumentsText.Length > 4096)
            {
                return InvalidResponse();
            }

            using var arguments = JsonDocument.Parse(argumentsText, new JsonDocumentOptions { MaxDepth = 16 });
            parsed.Add(new LlmToolCall(RequestChecks.LlmToolCallId(call.GetProperty("id").GetString()!),
                AgentChecks.McpToolName(function.GetProperty("name").GetString()!), arguments.RootElement));
        }

        return parsed.Select(call => call.Id).Distinct().Count() == parsed.Count
            ? new LlmCompletionResult.ToolCalls(parsed.AsReadOnly()) : InvalidResponse();
    }

    private static LlmTokenUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return new LlmTokenUsage(
            ReadCount(usage, "prompt_tokens"),
            ReadCount(usage, "completion_tokens"),
            ReadCount(usage, "total_tokens"));
    }

    private static int ReadCount(JsonElement usage, string name)
    {
        if (!usage.TryGetProperty(name, out var count) || !count.TryGetInt32(out var value))
        {
            throw new JsonException("Некорректная статистика использования токенов.");
        }

        return value;
    }

    private static LlmCompletionResult InvalidResponse()
    {
        return new LlmCompletionResult.Failure(new AppError(ErrorCode.InvalidLlmResponse,
            "Сервис модели вернул некорректный ответ. Попробуйте позже."));
    }

    private static LlmCompletionResult EmptyResponse()
    {
        return new LlmCompletionResult.Failure(new AppError(ErrorCode.EmptyLlmResponse,
            "Модель не вернула текст ответа. Попробуйте изменить запрос."));
    }

    public static AppError MapStatus(int status)
    {
        return status switch
        {
            401 => new AppError(ErrorCode.LlmUnauthorized, "Сервис модели отклонил ключ доступа. Проверьте настройки OpenRouter."),
            402 => new AppError(ErrorCode.LlmCreditLimit, "Лимит аккаунта модели исчерпан. Проверьте доступную квоту OpenRouter."),
            403 => new AppError(ErrorCode.LlmForbidden, "Сервис модели отклонил запрос. Попробуйте изменить формулировку."),
            408 or 504 => new AppError(ErrorCode.LlmTimeout, "Модель не успела ответить. Попробуйте позже.", IsRetryable: true),
            429 => new AppError(ErrorCode.LlmRateLimited, "Слишком много запросов к модели. Попробуйте позже.", IsRetryable: true),
            >= 500 and <= 599 => new AppError(ErrorCode.LlmUnavailable, "Сервис модели временно недоступен. Попробуйте позже.", IsRetryable: true),
            _ => new AppError(ErrorCode.LlmRejectedRequest, "Сервис модели не принял запрос. Проверьте настройки или сократите сообщение."),
        };
    }

    public static AppError MapException(Exception error)
    {
        return error switch
        {
            OperationCanceledException or TimeoutException => new AppError(ErrorCode.LlmTimeout,
                "Модель не успела ответить. Попробуйте позже.", IsRetryable: true),
            JsonException => new AppError(ErrorCode.InvalidLlmResponse, "Сервис модели вернул некорректный ответ. Попробуйте позже."),
            _ => new AppError(ErrorCode.LlmUnavailable, "Не удалось связаться с сервисом модели. Попробуйте позже.", IsRetryable: true),
        };
    }

    public static HttpClient CreateHttpClient(LlmSettings settings)
    {
        return new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
        })
        {
            BaseAddress = settings.ApiAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
