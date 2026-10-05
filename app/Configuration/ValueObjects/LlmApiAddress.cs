using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Configuration.ValueObjects;

public sealed record class LlmApiAddress
{
    public LlmApiAddress(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidLlmApiAddress,
                "LLM_API_BASE_URL: укажите HTTPS-адрес API без учётных данных, query и fragment; HTTP допускается только для локального сервиса.");
        }

        Value = new Uri(uri.AbsoluteUri.TrimEnd('/') + '/');
    }

    public Uri Value
    {
        get;
    }

    public override string ToString()
    {
        return Value.AbsoluteUri;
    }
}
