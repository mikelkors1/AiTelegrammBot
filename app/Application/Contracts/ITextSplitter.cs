using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface ITextSplitter
{
    IReadOnlyList<TelegramText> Split(LlmText text);
}
