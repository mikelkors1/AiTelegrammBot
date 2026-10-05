using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed class LlmRequest
{
    public LlmRequest(IEnumerable<LlmMessage> messages, Temperature temperature)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(temperature);
        var snapshot = messages.ToArray();

        if (snapshot.Length == 0 || snapshot.Any(message => message is null))
        {
            throw new ArgumentException("Запрос модели должен содержать непустой список сообщений.", nameof(messages));
        }

        Messages = Array.AsReadOnly(snapshot);
        Temperature = temperature;
    }

    public IReadOnlyList<LlmMessage> Messages
    {
        get;
    }
    public Temperature Temperature
    {
        get;
    }
}
