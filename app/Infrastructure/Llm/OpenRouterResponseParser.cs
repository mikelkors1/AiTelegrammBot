using System.Text.Json;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Infrastructure.Llm.Contracts;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Infrastructure.Llm;

public sealed class OpenRouterResponseParser(ILlmErrorMapper errorMapper) : ILlmResponseParser
{
    public LlmCompletionResult Parse(JsonElement root)
    {
        try
        {
            return ParseCore(root);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException
            or OverflowException or ValueObjectValidationException)
        {
            return InvalidResponse();
        }
    }

    private LlmCompletionResult ParseCore(JsonElement root)
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
                return new LlmCompletionResult.Failure(errorMapper.MapStatus(status));
            }

            return InvalidResponse();
        }

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1)
        {
            return InvalidResponse();
        }

        return ParseChoice(root, choices[0]);
    }

    private LlmCompletionResult ParseChoice(JsonElement root, JsonElement choice)
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
            return new LlmCompletionResult.Failure(errorMapper.MapStatus(403));
        }

        if (finish.GetString() != "stop" || !choice.TryGetProperty("message", out var message))
        {
            return InvalidResponse();
        }

        return ParseMessage(root, message);
    }

    private LlmCompletionResult ParseMessage(JsonElement root, JsonElement message)
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

        var provider = root.TryGetProperty("provider", out var providerValue) && providerValue.ValueKind != JsonValueKind.Null
            ? new LlmProviderName(providerValue.GetString()!) : null;
        return new LlmCompletionResult.Success(new LlmResponse(
            new LlmText(text), new ModelName(model.GetString()!), ParseUsage(root), provider));
    }

    private LlmTokenUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return new LlmTokenUsage(
            new TokenCount(ReadCount(usage, "prompt_tokens")),
            new TokenCount(ReadCount(usage, "completion_tokens")),
            new TokenCount(ReadCount(usage, "total_tokens")));
    }

    private int ReadCount(JsonElement usage, string name)
    {
        if (!usage.TryGetProperty(name, out var count) || !count.TryGetInt32(out var value))
        {
            throw new JsonException("Некорректная статистика использования токенов.");
        }

        return value;
    }

    private LlmCompletionResult InvalidResponse()
    {
        return new LlmCompletionResult.Failure(new AppError(ErrorCode.InvalidLlmResponse,
            "Сервис модели вернул некорректный ответ. Попробуйте позже."));
    }

    private LlmCompletionResult EmptyResponse()
    {
        return new LlmCompletionResult.Failure(new AppError(ErrorCode.EmptyLlmResponse,
            "Модель не вернула текст ответа. Попробуйте изменить запрос."));
    }
}
