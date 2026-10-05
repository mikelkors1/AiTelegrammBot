using System.Text.RegularExpressions;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Configuration.ValueObjects;

public sealed record class ProxyAddress
{
    public ProxyAddress(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "socks5")
            || uri.Host.Length == 0
            || !Regex.IsMatch(value, @":\d+/?$")
            || uri.Port is < 1 or > 65535
            || uri.AbsolutePath is not ("" or "/")
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            throw new ValueObjectValidationException(
                ErrorCode.InvalidProxy,
                "TELEGRAM_PROXY_URL: нужен http://host:port или socks5://host:port; при необходимости добавьте user:password@.");
        }

        Value = uri;
    }

    public Uri Value
    {
        get;
    }

    public override string ToString()
    {
        return "[скрыто]";
    }
}
