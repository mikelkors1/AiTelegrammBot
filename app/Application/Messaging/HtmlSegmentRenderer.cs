using System.Text;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Messaging;

public sealed class HtmlSegmentRenderer : IHtmlSegmentRenderer
{
    private const int MaximumLength = 4096;

    public IReadOnlyList<TelegramText> Render(ReplySegment segment)
    {
        var prefix = Prefix(segment);
        var suffix = segment.IsCode ? "</code></pre>" : "";
        var capacity = MaximumLength - prefix.Length - suffix.Length;
        var parts = new List<TelegramText>();
        var content = new StringBuilder();
        var offset = 0;
        while (offset < segment.Text.Length)
        {
            var length = char.IsHighSurrogate(segment.Text[offset]) && offset + 1 < segment.Text.Length
                && char.IsLowSurrogate(segment.Text[offset + 1]) ? 2 : 1;
            var escaped = Escape(segment.Text.Substring(offset, length));
            if (content.Length + escaped.Length > capacity)
            {
                parts.Add(new TelegramText(prefix + content + suffix));
                content.Clear();
            }

            content.Append(escaped);
            offset += length;
        }

        if (content.Length > 0)
        {
            parts.Add(new TelegramText(prefix + content + suffix));
        }

        return parts.AsReadOnly();
    }

    private string Prefix(ReplySegment segment)
    {
        if (!segment.IsCode)
        {
            return "";
        }

        return segment.Language is { } language
            ? $"<pre><code class=\"language-{language.Value}\">"
            : "<pre><code>";
    }

    private string Escape(string character)
    {
        return character switch
        {
            "&" => "&amp;",
            "<" => "&lt;",
            ">" => "&gt;",
            "\"" => "&quot;",
            _ => character,
        };
    }
}
