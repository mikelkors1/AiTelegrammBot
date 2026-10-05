using System.Net.Http.Json;
using ItmoBot.Application.Models;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Llm.Contracts;

namespace ItmoBot.Infrastructure.Llm;

public sealed class OpenRouterRequestWriter : ILlmRequestWriter
{
    public HttpContent CreateContent(LlmRequest request, LlmSettings settings)
    {
        return JsonContent.Create(new
        {
            model = settings.Model.Value,
            messages = request.Messages.Select(message => new
            {
                role = message.Role.ToString().ToLowerInvariant(),
                content = message.Content.Value,
            }),
            temperature = request.Temperature.Value,
            max_tokens = settings.MaxOutputTokens.Value,
            stream = false,
            reasoning = new
            {
                enabled = false
            },
            provider = new
            {
                require_parameters = true,
                allow_fallbacks = false
            },
        });
    }
}
