using ItmoBot.Configuration.Secrets;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Configuration.Models;

public sealed class LlmSettings
{
    public LlmSettings(Uri apiAddress, LlmApiKey apiKey, string model,
        TimeSpan timeout, int maxOutputTokens,
        int contextTokenLimit, int historyMessageLimit, TimeSpan retryWindow,
        LlmStructuredOutputMode structuredOutputMode = LlmStructuredOutputMode.JsonSchema)
    {
        if (maxOutputTokens >= contextTokenLimit)
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration,
                "LLM_MAX_OUTPUT_TOKENS должен быть меньше HISTORY_MAX_TOKENS, чтобы оставалось место для входного контекста.");
        }

        if (!Enum.IsDefined(structuredOutputMode))
        {
            throw new InputValidationException(ErrorCode.InvalidConfiguration, "Неизвестный режим структурированного ответа.");
        }

        StructuredOutputMode = structuredOutputMode;
        ApiAddress = apiAddress;
        ApiKey = apiKey;
        Model = model;
        Timeout = timeout;
        MaxOutputTokens = maxOutputTokens;
        ContextTokenLimit = contextTokenLimit;
        HistoryMessageLimit = historyMessageLimit;
        RetryWindow = retryWindow;
    }

    public Uri ApiAddress
    {
        get;
    }
    public TimeSpan RetryWindow
    {
        get;
    }
    public LlmApiKey ApiKey
    {
        get;
    }
    public string Model
    {
        get;
    }
    public TimeSpan Timeout
    {
        get;
    }
    public int MaxOutputTokens
    {
        get;
    }
    public int ContextTokenLimit
    {
        get;
    }
    public int HistoryMessageLimit
    {
        get;
    }
    public LlmStructuredOutputMode StructuredOutputMode
    {
        get;
    }

}
