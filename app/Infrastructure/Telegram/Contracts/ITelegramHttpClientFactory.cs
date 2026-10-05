using ItmoBot.Configuration.Models;

namespace ItmoBot.Infrastructure.Telegram.Contracts;

public interface ITelegramHttpClientFactory
{
    HttpClient Create(Settings settings);
}
