using System.Net;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Telegram.Contracts;

namespace ItmoBot.Infrastructure.Telegram;

public sealed class TelegramHttpClientFactory : ITelegramHttpClientFactory
{
    public HttpClient Create(Settings settings)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = settings.TelegramProxy is not null
        };
        if (handler.UseProxy)
        {
            var uri = settings.TelegramProxy!.Value;
            var proxy = new WebProxy(new UriBuilder(uri) { UserName = "", Password = "" }.Uri);
            if (uri.UserInfo.Length > 0)
            {
                var parts = uri.UserInfo.Split(':', 2);
                proxy.Credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]), parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "");
            }

            handler.Proxy = proxy;
        }

        // Без обхода заданного прокси при сбое. Один клиент для всех Telegram-вызовов.
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(40)
        };
    }

}
