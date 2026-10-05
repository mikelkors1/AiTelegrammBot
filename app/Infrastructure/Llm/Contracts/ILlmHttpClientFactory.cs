using ItmoBot.Configuration.Models;

namespace ItmoBot.Infrastructure.Llm.Contracts;

public interface ILlmHttpClientFactory
{
    HttpClient Create(LlmSettings settings);
}
