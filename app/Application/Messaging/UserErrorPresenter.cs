using ItmoBot.Shared.Errors;
using ItmoBot.Validation;

namespace ItmoBot.Application.Messaging;

public sealed class UserErrorPresenter
{
    public string Format(AppError error)
    {
        var message = error.Code switch
        {
            ErrorCode.UnknownBotCommand => "Неизвестная команда. Доступны /start, /study, /translate, /quiz, /settings, /reset, /tools, /timezone и /why.",
            ErrorCode.InvalidBotCommandArgument => "Эта команда не принимает аргументы. Для настройки используйте /settings 0.7 или /timezone Europe/Moscow.",
            ErrorCode.InvalidTemperature => "Выберите коэффициент креативности ответа: /settings 0.0, /settings 0.3, /settings 0.7 или /settings 1.0.",
            ErrorCode.AgentToolLimit => "Достигнут лимит двух вызовов инструментов. Разделите запрос на несколько сообщений.",
            ErrorCode.McpUnavailable or ErrorCode.McpTimeout => "Внешняя функция сейчас недоступна. Попробуйте позже.",
            ErrorCode.InvalidMcpResponse => "Внешняя функция вернула некорректные данные. Попробуйте позже.",
            ErrorCode.InvalidArguments => "Не удалось однозначно определить параметры действия. Уточните запрос.",
            ErrorCode.InvalidTimeZone => "Неизвестный часовой пояс. Используйте зону IANA, например /timezone Europe/Moscow.",
            ErrorCode.InvalidCallback => "Кнопка не распознана. Отправьте новый запрос на напоминание.",
            ErrorCode.ActionExpired => "Срок подтверждения истёк. Отправьте новый запрос на напоминание.",
            ErrorCode.ActionCancelled => "Это действие уже отменено. Для нового напоминания отправьте новый запрос.",
            ErrorCode.ActionAlreadyConfirmed => "Действие уже подтверждено. Нажмите «Подтвердить», чтобы безопасно проверить результат.",
            ErrorCode.TimeZoneNotSet => "Сначала выберите часовой пояс: /timezone Europe/Moscow.",
            ErrorCode.ReminderTimeInPast => "Время напоминания должно быть в будущем.",
            ErrorCode.ActionMismatch => "Параметры действия изменились. Подготовьте запрос заново.",
            ErrorCode.Unauthorized => "Нет полномочий для выполнения этого действия.",
            ErrorCode.LlmRejectedRequest => "Модель отклонила формат запроса. Проверьте поддержку инструментов и структурированных ответов у выбранного провайдера.",
            ErrorCode.InvalidLlmText => "Отправьте сообщение с текстом.",
            ErrorCode.ContextTooLarge => "Запрос слишком длинный для доступного контекста. Сократите текст и отправьте его снова.",
            ErrorCode.InvalidContext => "Не удалось восстановить контекст. Выполните /reset и повторите запрос.",
            ErrorCode.ConversationChanged or ErrorCode.ConversationTurnNotFound => "Диалог изменился во время обработки. Отправьте запрос заново.",
            ErrorCode.LlmTimeout => "Модель не успела ответить. Попробуйте отправить запрос позже.",
            ErrorCode.LlmRetryTimeout => "Модель не ответила за отведённое время. Попробуйте позже.",
            ErrorCode.LlmDailyQuotaExceeded => "Суточный лимит запросов к модели исчерпан. Попробуйте после обновления квоты OpenRouter.",
            ErrorCode.LlmRateLimited => "Сервис модели временно ограничил запросы. Попробуйте позже.",
            ErrorCode.LlmUnauthorized or ErrorCode.LlmForbidden or ErrorCode.LlmCreditLimit => "Сервис модели сейчас недоступен. Сообщите администратору бота.",
            ErrorCode.EmptyLlmResponse or ErrorCode.InvalidLlmResponse or ErrorCode.IncompleteLlmResponse => "Модель не вернула полный корректный ответ. Попробуйте повторить запрос.",
            ErrorCode.LlmUnavailable => "Не удалось связаться с моделью. Попробуйте позже.",
            ErrorCode.DatabaseUnavailable or ErrorCode.DatabaseTimeout or ErrorCode.DatabaseOperationFailed or ErrorCode.DatabaseAuthenticationFailed => "Не удалось сохранить или прочитать диалог. Попробуйте позже.",
            _ => "Не удалось обработать запрос. Попробуйте позже.",
        };
        return RequestChecks.LlmText(message);
    }

    public string DeliveryFailure()
    {
        return RequestChecks.LlmText("Не удалось доставить ответ полностью. Автоматическая повторная генерация не выполняется.");
    }
}
