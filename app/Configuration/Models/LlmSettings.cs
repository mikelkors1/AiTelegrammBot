using ItmoBot.Application.ValueObjects;
using ItmoBot.Configuration.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Configuration.Models;

public sealed class LlmSettings
{
    public LlmSettings(LlmApiAddress apiAddress, LlmApiKey apiKey, ModelName model,
        LlmRequestTimeout timeout, TokenLimit maxOutputTokens,
        TokenLimit contextTokenLimit, HistoryMessageLimit historyMessageLimit, LlmRetryWindow retryWindow)
    {
        if (maxOutputTokens.Value >= contextTokenLimit.Value)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidConfiguration,
                "LLM_MAX_OUTPUT_TOKENS должен быть меньше HISTORY_MAX_TOKENS, чтобы оставалось место для входного контекста.");
        }

        ApiAddress = apiAddress;
        ApiKey = apiKey;
        Model = model;
        Timeout = timeout;
        MaxOutputTokens = maxOutputTokens;
        ContextTokenLimit = contextTokenLimit;
        HistoryMessageLimit = historyMessageLimit;
        RetryWindow = retryWindow;
    }

    public LlmApiAddress ApiAddress
    {
        get;
    }
    public LlmRetryWindow RetryWindow
    {
        get;
    }
    public LlmApiKey ApiKey
    {
        get;
    }
    public ModelName Model
    {
        get;
    }
    public LlmRequestTimeout Timeout
    {
        get;
    }
    public TokenLimit MaxOutputTokens
    {
        get;
    }
    public TokenLimit ContextTokenLimit
    {
        get;
    }
    public HistoryMessageLimit HistoryMessageLimit
    {
        get;
    }
}
