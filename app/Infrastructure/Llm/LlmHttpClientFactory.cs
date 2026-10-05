using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Llm.Contracts;

namespace ItmoBot.Infrastructure.Llm;

public sealed class LlmHttpClientFactory : ILlmHttpClientFactory
{
    public HttpClient Create(LlmSettings settings)
    {
        return new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
        })
        {
            BaseAddress = settings.ApiAddress.Value,
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
