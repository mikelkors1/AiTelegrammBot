using System.Text.Json;
using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Infrastructure.Llm.Contracts;

public interface ILlmResponseParser
{
    LlmCompletionResult Parse(JsonElement root);
}
