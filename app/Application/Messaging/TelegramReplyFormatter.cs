using System.Text;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Messaging;

public sealed class TelegramReplyFormatter(
    ICodeBlockParser parser,
    IHtmlSegmentRenderer renderer,
    ITextSplitter plainSplitter) : ITelegramReplyFormatter
{
    public IReadOnlyList<OutgoingTelegramMessage> Format(LlmText text)
    {
        var segments = parser.Parse(text);
        if (segments.All(segment => !segment.IsCode))
        {
            return Array.AsReadOnly(plainSplitter.Split(text)
                .Select(part => new OutgoingTelegramMessage(part, TelegramTextFormat.Plain)).ToArray());
        }

        var messages = new List<OutgoingTelegramMessage>();
        var current = new StringBuilder();
        foreach (var segment in segments)
        {
            foreach (var part in renderer.Render(segment))
            {
                if (current.Length + part.Value.Length > 4096)
                {
                    messages.Add(new OutgoingTelegramMessage(new TelegramText(current.ToString()), TelegramTextFormat.Html));
                    current.Clear();
                }

                current.Append(part.Value);
            }
        }

        if (current.Length > 0)
        {
            messages.Add(new OutgoingTelegramMessage(new TelegramText(current.ToString()), TelegramTextFormat.Html));
        }

        return messages.AsReadOnly();
    }
}
