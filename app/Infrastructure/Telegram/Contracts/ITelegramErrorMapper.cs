using ItmoBot.Shared.Errors;

namespace ItmoBot.Infrastructure.Telegram.Contracts;

public interface ITelegramErrorMapper
{
    AppError Map(Exception error);
}
