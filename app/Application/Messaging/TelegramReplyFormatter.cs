using System.Text.RegularExpressions;
using System.Text;
using ItmoBot.Validation;

namespace ItmoBot.Application.Messaging;

public sealed class TelegramReplyFormatter
{
    public IReadOnlyList<OutgoingTelegramMessage> Format(string text)
    {
        var segments = ParseCodeBlocks(text);
        if (segments.All(segment => !segment.IsCode))
        {
            return Array.AsReadOnly(SplitText(text)
                .Select(part => new OutgoingTelegramMessage(part, TelegramTextFormat.Plain)).ToArray());
        }

        var messages = new List<OutgoingTelegramMessage>();
        var current = new StringBuilder();
        foreach (var segment in segments)
        {
            foreach (string part in RenderSegment(segment))
            {
                if (current.Length + part.Length > 4096)
                {
                    messages.Add(new OutgoingTelegramMessage(TelegramChecks.TelegramText(current.ToString()), TelegramTextFormat.Html));
                    current.Clear();
                }

                current.Append(part);
            }
        }

        if (current.Length > 0)
        {
            messages.Add(new OutgoingTelegramMessage(TelegramChecks.TelegramText(current.ToString()), TelegramTextFormat.Html));
        }

        return messages.AsReadOnly();
    }

    private readonly Regex blocks = new(
        @"^[ \t]{0,3}```(?<language>[A-Za-z0-9_+#-]{0,40})[ \t]*\r?\n(?<code>[\s\S]*?)^[ \t]{0,3}```[ \t]*(?=\r?$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public IReadOnlyList<ReplySegment> ParseCodeBlocks(string text)
    {
        try
        {
            return ParseCodeBlocksCore(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return Array.AsReadOnly(new[] { new ReplySegment(text, false, null) });
        }
    }

    private IReadOnlyList<ReplySegment> ParseCodeBlocksCore(string text)
    {
        var segments = new List<ReplySegment>();
        var offset = 0;
        foreach (Match match in blocks.Matches(text))
        {
            if (match.Index > offset)
            {
                segments.Add(new ReplySegment(text[offset..match.Index], false, null));
            }

            var code = match.Groups["code"].Value;
            var language = match.Groups["language"].Value;
            if (code.Length == 0)
            {
                segments.Add(new ReplySegment(match.Value, false, null));
            }
            else
            {
                segments.Add(new ReplySegment(code, true, language.Length == 0 ? null : TelegramChecks.CodeLanguage(language)));
            }

            offset = match.Index + match.Length;
        }

        if (offset < text.Length)
        {
            segments.Add(new ReplySegment(text[offset..], false, null));
        }

        return segments.AsReadOnly();
    }

    private const int MaximumLength = 4096;

    public IReadOnlyList<string> RenderSegment(ReplySegment segment)
    {
        var prefix = Prefix(segment);
        var suffix = segment.IsCode ? "</code></pre>" : "";
        var capacity = MaximumLength - prefix.Length - suffix.Length;
        var parts = new List<string>();
        var content = new StringBuilder();
        var offset = 0;
        while (offset < segment.Text.Length)
        {
            var length = char.IsHighSurrogate(segment.Text[offset]) && offset + 1 < segment.Text.Length
                && char.IsLowSurrogate(segment.Text[offset + 1]) ? 2 : 1;
            var escaped = Escape(segment.Text.Substring(offset, length));
            if (content.Length + escaped.Length > capacity)
            {
                parts.Add(TelegramChecks.TelegramText(prefix + content + suffix));
                content.Clear();
            }

            content.Append(escaped);
            offset += length;
        }

        if (content.Length > 0)
        {
            parts.Add(TelegramChecks.TelegramText(prefix + content + suffix));
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
            ? $"<pre><code class=\"language-{language}\">"
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


    public IReadOnlyList<string> SplitText(string text)
    {
        var parts = new List<string>();
        var offset = 0;
        while (offset < text.Length)
        {
            var length = Math.Min(MaximumLength, text.Length - offset);
            if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])
                && char.IsLowSurrogate(text[offset + length]))
            {
                length--;
            }

            var part = text.Substring(offset, length);
            parts.Add(TelegramChecks.TelegramText(part));
            offset += length;
        }

        return parts.AsReadOnly();
    }

}
