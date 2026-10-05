using ItmoBot.Application.Models;
using ItmoBot.Configuration.Models;

namespace ItmoBot.Infrastructure.Llm.Contracts;

public interface ILlmRequestWriter
{
    HttpContent CreateContent(LlmRequest request, LlmSettings settings);
}
