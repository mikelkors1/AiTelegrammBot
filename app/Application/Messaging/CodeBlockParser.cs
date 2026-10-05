using System.Text.RegularExpressions;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Messaging;

public sealed class CodeBlockParser : ICodeBlockParser
{
    private readonly Regex blocks = new(
        @"^[ \t]{0,3}```(?<language>[A-Za-z0-9_+#-]{0,40})[ \t]*\r?\n(?<code>[\s\S]*?)^[ \t]{0,3}```[ \t]*(?=\r?$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public IReadOnlyList<ReplySegment> Parse(LlmText text)
    {
        try
        {
            return ParseCore(text.Value);
        }
        catch (RegexMatchTimeoutException)
        {
            return Array.AsReadOnly(new[] { new ReplySegment(text.Value, false, null) });
        }
    }

    private IReadOnlyList<ReplySegment> ParseCore(string text)
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
                segments.Add(new ReplySegment(code, true, language.Length == 0 ? null : new CodeLanguage(language)));
            }

            offset = match.Index + match.Length;
        }

        if (offset < text.Length)
        {
            segments.Add(new ReplySegment(text[offset..], false, null));
        }

        return segments.AsReadOnly();
    }
}
