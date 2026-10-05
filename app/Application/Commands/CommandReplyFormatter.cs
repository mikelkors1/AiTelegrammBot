using System.Globalization;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Commands;

public sealed class CommandReplyFormatter(ModelName model) : ICommandReplyFormatter
{
    public LlmText Welcome(ConversationState state)
    {
        return new LlmText($"""
            Привет! Я AI-ассистент студента: объясняю программирование, перевожу тексты и провожу тренировочные опросы.

            /study — объяснение учебных вопросов
            /translate — перевод на указанный язык
            /quiz — вопросы и разбор ваших ответов
            /settings — текущие настройки
            /settings 0.7 — изменить коэффициент креативности ответа
            /reset — очистить историю

            Доступные значения креативности: 0.0, 0.3, 0.7, 1.0.
            Текущий режим: {ModeName(state.Mode)}. Отправьте обычное текстовое сообщение.
            Ответы модели могут содержать ошибки — проверяйте важные сведения.
            """);
    }

    public LlmText Settings(ConversationState state)
    {
        return new LlmText($"""
            Режим: {ModeName(state.Mode)}
            Модель: {model.Value}
            Коэффициент креативности ответа: {state.Temperature.Value.ToString("0.0", CultureInfo.InvariantCulture)}
            Для изменения: /settings 0.0, /settings 0.3, /settings 0.7 или /settings 1.0.
            """);
    }

    public LlmText ModeChanged(ConversationState state)
    {
        return new LlmText($"Режим переключён: {ModeName(state.Mode)}. История очищена. Коэффициент креативности ответа сохранён.");
    }

    public LlmText Reset(ConversationState state)
    {
        return new LlmText("История очищена. Режим и коэффициент креативности ответа сохранены.");
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
