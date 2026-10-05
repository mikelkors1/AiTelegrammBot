using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IContextTokenEstimator
{
    EstimatedTokenCount Estimate(IReadOnlyList<LlmMessage> messages);
}
