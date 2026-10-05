using ItmoBot.Application.Contracts;
using ItmoBot.Configuration.Models;
using System.Text.RegularExpressions;

namespace ItmoBot.Infrastructure.Logging;

public sealed class SecretRedactor : ISecretRedactor
{
    private readonly string[] secrets;
    public SecretRedactor(Settings settings)
    {
        var items = new List<string>
        {
            settings.BotToken.Value,
            settings.PostgresPassword.Value,
            settings.Llm.ApiKey.Value,
            settings.TelegramProxy?.Value.OriginalString ?? ""
        };
        if (Uri.TryCreate(settings.TelegramProxy?.Value.OriginalString ?? "", UriKind.Absolute, out var proxy))
        {
            items.Add(proxy.UserInfo);
            items.Add(Uri.UnescapeDataString(proxy.UserInfo));
            var separator = proxy.UserInfo.IndexOf(':');
            if (separator >= 0)
            {
                items.Add(proxy.UserInfo[(separator + 1)..]);
                items.Add(Uri.UnescapeDataString(proxy.UserInfo[(separator + 1)..]));
            }
        }

        secrets = items.Where(s => s.Length > 0).Distinct().OrderByDescending(s => s.Length).ToArray();
    }

    public string Redact(string text)
    {
        foreach (var secret in secrets)
        {
            text = text.Replace(secret, "[скрыто]", StringComparison.Ordinal);
        }

        return Regex.Replace(text, @"(https?://|socks5://)[^\s/@]+:[^\s/@]+@", "$1[скрыто]@");
    }
}
