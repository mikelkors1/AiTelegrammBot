using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed record class LlmResponse(LlmText Text, ModelName Model, LlmTokenUsage? Usage, LlmProviderName? Provider);
