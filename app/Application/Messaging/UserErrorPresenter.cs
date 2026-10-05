using ItmoBot.Application.Contracts;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Messaging;

public sealed class UserErrorPresenter : IUserErrorPresenter
{
    public LlmText Format(AppError error)
    {
        var message = error.Code switch
        {
            ErrorCode.UnknownBotCommand => "Неизвестная команда. Доступны /start, /study, /translate, /quiz, /settings и /reset.",
            ErrorCode.InvalidBotCommandArgument => "Эта команда не принимает аргументы. Для изменения креативности используйте /settings 0.7.",
            ErrorCode.InvalidTemperature => "Выберите коэффициент креативности ответа: /settings 0.0, /settings 0.3, /settings 0.7 или /settings 1.0.",
            ErrorCode.InvalidLlmText => "Отправьте сообщение с текстом.",
            ErrorCode.ContextTooLarge => "Запрос слишком длинный для доступного контекста. Сократите текст и отправьте его снова.",
            ErrorCode.InvalidContext => "Не удалось восстановить контекст. Выполните /reset и повторите запрос.",
            ErrorCode.ConversationChanged or ErrorCode.ConversationTurnNotFound => "Диалог изменился во время обработки. Отправьте запрос заново.",
            ErrorCode.LlmTimeout => "Модель не успела ответить. Попробуйте отправить запрос позже.",
            ErrorCode.LlmRetryTimeout => "Модель не ответила за отведённое время. Попробуйте позже.",
            ErrorCode.LlmRateLimited => "Сервис модели временно ограничил запросы. Попробуйте позже.",
            ErrorCode.LlmUnauthorized or ErrorCode.LlmForbidden or ErrorCode.LlmCreditLimit => "Сервис модели сейчас недоступен. Сообщите администратору бота.",
            ErrorCode.EmptyLlmResponse or ErrorCode.InvalidLlmResponse or ErrorCode.IncompleteLlmResponse => "Модель не вернула полный корректный ответ. Попробуйте повторить запрос.",
            ErrorCode.LlmUnavailable => "Не удалось связаться с моделью. Попробуйте позже.",
            ErrorCode.DatabaseUnavailable or ErrorCode.DatabaseTimeout or ErrorCode.DatabaseOperationFailed or ErrorCode.DatabaseAuthenticationFailed => "Не удалось сохранить или прочитать диалог. Попробуйте позже.",
            _ => "Не удалось обработать запрос. Попробуйте позже.",
        };
        return new LlmText(message);
    }

    public LlmText DeliveryFailure()
    {
        return new LlmText("Не удалось доставить ответ полностью. Автоматическая повторная генерация не выполняется.");
    }
}
