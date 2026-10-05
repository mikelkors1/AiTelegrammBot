using ItmoBot.Infrastructure.Telegram.Contracts;
using System.Text.Json;
using ItmoBot.Shared.Errors;
using Telegram.Bot.Exceptions;

namespace ItmoBot.Infrastructure.Telegram;

public sealed class TelegramErrorMapper : ITelegramErrorMapper
{
    public AppError Map(Exception error)
    {
        if (error is RequestException and not ApiRequestException && error.InnerException is { } inner)
        {
            return Map(inner);
        }

        return error switch
        {
            ApiRequestException { ErrorCode: 401 } => new AppError(ErrorCode.TelegramUnauthorized, "Telegram отклонил токен. Проверьте BOT_TOKEN."),
            ApiRequestException { ErrorCode: 403 } => new AppError(ErrorCode.TelegramForbidden, "Telegram запретил действие. Проверьте права бота и доступ к чату."),
            ApiRequestException { ErrorCode: 409 } => new AppError(ErrorCode.TelegramPollingConflict, "Telegram обнаружил другой polling-процесс. Остановите второй экземпляр бота."),
            ApiRequestException { ErrorCode: 429 } => new AppError(ErrorCode.TelegramRateLimited, "Превышен лимит запросов Telegram. Повторите позже.", IsRetryable: true),
            ApiRequestException { ErrorCode: >= 500 } => new AppError(ErrorCode.TelegramUnavailable, "Сервис Telegram временно недоступен. Повторите позже.", IsRetryable: true),
            ApiRequestException => new AppError(ErrorCode.TelegramRejectedRequest, "Telegram отклонил запрос. Проверьте параметры сообщения и права бота."),
            OperationCanceledException or TimeoutException => new AppError(ErrorCode.TelegramTimeout, "Истекло время ожидания Telegram. Проверьте сеть и прокси.", IsRetryable: true),
            JsonException => new AppError(ErrorCode.TelegramRejectedRequest, "Telegram вернул некорректный ответ."),
            _ => new AppError(ErrorCode.TelegramUnavailable, "Не удалось подключиться к Telegram. Проверьте сеть и прокси.", IsRetryable: true),
        };
    }
}
