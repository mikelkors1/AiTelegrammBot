using System.Text;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Context;

public sealed class Utf8ContextTokenEstimator : IContextTokenEstimator
{
    private const int RequestOverhead = 32;
    private const int MessageOverhead = 12;
    private const int SafetyMargin = 256;

    public EstimatedTokenCount Estimate(IReadOnlyList<LlmMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        // Deliberately conservative heuristic, not the model's tokenizer or usage.
        long estimate = RequestOverhead + SafetyMargin;
        foreach (var message in messages)
        {
            estimate += Encoding.UTF8.GetByteCount(message.Content.Value) + (long)MessageOverhead;
        }

        return new EstimatedTokenCount(estimate);
    }
}
