using System.Text.Json;
using ItmoBot.Application.Mcp;

namespace ItmoBot.Application.Llm;

public sealed class LlmRequest
{
    public LlmRequest(IEnumerable<LlmMessage> messages, decimal temperature, IReadOnlyList<McpToolDefinition>? tools = null,
        JsonElement? finalResponseSchema = null, int mandatoryMessageCount = 0, int historyMessageCount = 0)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var snapshot = messages.ToArray();

        if (snapshot.Length == 0 || snapshot.Any(message => message is null))
        {
            throw new ArgumentException("Запрос модели должен содержать непустой список сообщений.", nameof(messages));
        }

        Messages = Array.AsReadOnly(snapshot);
        if (mandatoryMessageCount < 0 || mandatoryMessageCount > snapshot.Length
            || historyMessageCount < 0 || mandatoryMessageCount + historyMessageCount >= snapshot.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(mandatoryMessageCount), "Границы обязательного контекста и истории указаны неверно.");
        }

        MandatoryMessageCount = mandatoryMessageCount;
        HistoryMessageCount = historyMessageCount;
        Temperature = ItmoBot.Validation.RequestChecks.Temperature(temperature);
        Tools = Array.AsReadOnly((tools ?? []).ToArray());
        FinalResponseSchema = finalResponseSchema;
    }

    public IReadOnlyList<LlmMessage> Messages
    {
        get;
    }
    public decimal Temperature
    {
        get;
    }
    public IReadOnlyList<McpToolDefinition> Tools
    {
        get;
    }
    public JsonElement? FinalResponseSchema
    {
        get;
    }

    public int MandatoryMessageCount
    {
        get;
    }

    public int HistoryMessageCount
    {
        get;
    }

}
