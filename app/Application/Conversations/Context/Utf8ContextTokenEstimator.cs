using System.Text;
using ItmoBot.Application.Llm;

namespace ItmoBot.Application.Conversations.Context;

public sealed class Utf8ContextTokenEstimator
{
    private const int RequestOverhead = 32;
    private const int MessageOverhead = 12;
    private const int SafetyMargin = 256;

    public long Estimate(IReadOnlyList<LlmMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        // Deliberately conservative heuristic, not the model's tokenizer or usage.
        long estimate = RequestOverhead + SafetyMargin;
        foreach (var message in messages)
        {
            estimate += Encoding.UTF8.GetByteCount(message.Content) + (long)MessageOverhead;
        }

        return estimate;
    }
}
