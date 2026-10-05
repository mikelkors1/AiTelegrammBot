using ItmoBot.Application.Contracts;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Messaging;

public sealed class TelegramTextSplitter : ITextSplitter
{
    private const int MaximumLength = 4096;

    public IReadOnlyList<TelegramText> Split(LlmText text)
    {
        var parts = new List<TelegramText>();
        var offset = 0;
        while (offset < text.Value.Length)
        {
            var length = Math.Min(MaximumLength, text.Value.Length - offset);
            if (offset + length < text.Value.Length && char.IsHighSurrogate(text.Value[offset + length - 1])
                && char.IsLowSurrogate(text.Value[offset + length]))
            {
                length--;
            }

            var part = text.Value.Substring(offset, length);
            parts.Add(new TelegramText(part));
            offset += length;
        }

        return parts.AsReadOnly();
    }

}
