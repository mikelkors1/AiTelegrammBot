using System.Globalization;
using ItmoBot.Application.Conversations;
using ItmoBot.Validation;

namespace ItmoBot.Application.Commands;

public sealed class CommandReplyFormatter(string model)
{
    public string Welcome(ConversationState state)
    {
        return RequestChecks.LlmText($"""
            Привет! Я Nerdy AI: помогаю с учёбой, переводами и тренировочными опросами.

            /study — объяснение учебных вопросов
            /translate — перевод на указанный язык
            /quiz — вопросы и разбор ваших ответов
            /settings — текущие настройки
            /settings 0.7 — изменить коэффициент креативности ответа
            /reset — очистить историю
            /tools — инструменты MCP
            /timezone — часовой пояс (например /timezone Europe/Moscow)
            /why — последнее решение агента

            Доступные значения креативности: 0.0, 0.3, 0.7, 1.0.
            Текущий режим: {ModeName(state.Mode)}. Отправьте обычное текстовое сообщение.
            Ответы модели могут содержать ошибки — проверяйте важные сведения.
            """);
    }

    public string Settings(ConversationState state)
    {
        return RequestChecks.LlmText($"""
            Режим: {ModeName(state.Mode)}
            Модель: {model}
            Коэффициент креативности ответа: {state.Temperature.ToString("0.0", CultureInfo.InvariantCulture)}
            Для изменения: /settings 0.0, /settings 0.3, /settings 0.7 или /settings 1.0.
            """);
    }

    public string ModeChanged(ConversationState state)
    {
        return RequestChecks.LlmText($"Режим переключён: {ModeName(state.Mode)}. История очищена. Коэффициент креативности ответа сохранён.");
    }

    public string Reset(ConversationState state)
    {
        return RequestChecks.LlmText("История очищена. Режим и коэффициент креативности ответа сохранены.");
    }

    private string ModeName(AssistantMode mode)
    {
        return mode switch
        {
            AssistantMode.Study => "Учёба (/study)",
            AssistantMode.Translate => "Перевод (/translate)",
            AssistantMode.Quiz => "Тренировочный опрос (/quiz)",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }
}
