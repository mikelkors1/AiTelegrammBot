namespace ItmoBot.Application.Llm;

public sealed record class LlmResponse(string Text, string Model, LlmTokenUsage? Usage, string? Provider);
