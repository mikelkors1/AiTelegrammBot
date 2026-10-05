using System.Text.Json;
using ItmoBot.Infrastructure.Llm.Contracts;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Infrastructure.Llm;

public sealed class LlmErrorMapper : ILlmErrorMapper
{
    public AppError MapStatus(int status)
    {
        return status switch
        {
            401 => new AppError(ErrorCode.LlmUnauthorized, "Сервис модели отклонил ключ доступа. Проверьте настройки OpenRouter."),
            402 => new AppError(ErrorCode.LlmCreditLimit, "Лимит аккаунта модели исчерпан. Проверьте доступную квоту OpenRouter."),
            403 => new AppError(ErrorCode.LlmForbidden, "Сервис модели отклонил запрос. Попробуйте изменить формулировку."),
            408 or 504 => new AppError(ErrorCode.LlmTimeout, "Модель не успела ответить. Попробуйте позже.", IsRetryable: true),
            429 => new AppError(ErrorCode.LlmRateLimited, "Слишком много запросов к модели. Попробуйте позже.", IsRetryable: true),
            >= 500 and <= 599 => new AppError(ErrorCode.LlmUnavailable, "Сервис модели временно недоступен. Попробуйте позже.", IsRetryable: true),
            _ => new AppError(ErrorCode.LlmRejectedRequest, "Сервис модели не принял запрос. Проверьте настройки или сократите сообщение."),
        };
    }

    public AppError MapException(Exception error)
    {
        return error switch
        {
            OperationCanceledException or TimeoutException => new AppError(ErrorCode.LlmTimeout,
                "Модель не успела ответить. Попробуйте позже.", IsRetryable: true),
            JsonException => new AppError(ErrorCode.InvalidLlmResponse, "Сервис модели вернул некорректный ответ. Попробуйте позже."),
            _ => new AppError(ErrorCode.LlmUnavailable, "Не удалось связаться с сервисом модели. Попробуйте позже.", IsRetryable: true),
        };
    }
}
