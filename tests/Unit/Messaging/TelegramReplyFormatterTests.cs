using System.Xml.Linq;
using ItmoBot.Application.Messaging;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Messaging;

public sealed class TelegramReplyFormatterTests
{
    [Fact]
    public void Format_FencedCode_BecomesPreformattedHtmlWithLanguage()
    {
        // Arrange
        var source = "Пример:\n```csharp\nConsole.WriteLine(\"Привет\");\n```\nКонец.";

        // Act
        var actualResult = Formatter().Format(RequestChecks.LlmText(source));

        // Assert
        var message = Assert.Single(actualResult);

        Assert.Equal(TelegramTextFormat.Html, message.Format);
        var html = XDocument.Parse("<root>" + message.Text + "</root>");
        Assert.Equal("language-csharp", html.Descendants("code").Single().Attribute("class")!.Value);
        Assert.Equal("Console.WriteLine(\"Привет\");\n", html.Descendants("code").Single().Value);
        Assert.Equal("Пример:\nConsole.WriteLine(\"Привет\");\n\nКонец.", html.Root!.Value);
    }

    [Fact]
    public void Format_HtmlFromModel_IsEscapedBothInsideAndOutsideCode()
    {
        // Arrange
        var source = "<b>Текст & тест</b>\n```html\n</code></pre><a href=\"tg://user?id=1\">x</a>\n```";

        // Act
        var actualResult = Formatter().Format(RequestChecks.LlmText(source));

        // Assert
        var message = Assert.Single(actualResult);
        var html = XDocument.Parse("<root>" + message.Text + "</root>");

        Assert.Empty(html.Descendants("b"));
        Assert.Empty(html.Descendants("a"));
        Assert.Contains("<b>Текст & тест</b>", html.Root!.Value);
        Assert.Equal("</code></pre><a href=\"tg://user?id=1\">x</a>\n", html.Descendants("code").Single().Value);
    }

    [Fact]
    public void Format_LongCode_IsSplitIntoIndependentlyValidBlocksWithoutLosingContent()
    {
        // Arrange
        var code = string.Concat(Enumerable.Repeat("var x = \"😀<&>\";\n", 1000));

        // Act
        var messages = Formatter().Format(RequestChecks.LlmText("```csharp\n" + code + "```"));

        // Assert
        Assert.True(messages.Count > 1);
        var restored = new List<string>();
        foreach (var message in messages)
        {
            Assert.InRange(message.Text.Length, 1, 4096);
            Assert.Equal(TelegramTextFormat.Html, message.Format);
            var html = XDocument.Parse("<root>" + message.Text + "</root>");
            Assert.All(html.Descendants("code"), element => Assert.Equal("language-csharp", element.Attribute("class")!.Value));
            restored.Add(html.Root!.Value);
        }

        Assert.Equal(code, string.Concat(restored));
    }

    [Fact]
    public void Format_MultipleLanguagesAndAnonymousCode_AreHandled()
    {
        // Arrange
        var source = "```cs\nint x = 1;\n```\n```python\nprint(1)\n```\n```\nx\n```";

        // Act
        var actualResult = Formatter().Format(RequestChecks.LlmText(source));

        // Assert
        var message = Assert.Single(actualResult);
        var blocks = XDocument.Parse("<root>" + message.Text + "</root>").Descendants("code").ToArray();
        Assert.Equal(3, blocks.Length);
        Assert.Equal("language-csharp", blocks[0].Attribute("class")!.Value);
        Assert.Equal("language-python", blocks[1].Attribute("class")!.Value);
        Assert.Null(blocks[2].Attribute("class"));
    }

    [Theory]
    [InlineData("Текст <b> & без кода")]
    [InlineData("```csharp\nнезакрытый блок")]
    [InlineData("```csharp\n```")]
    [InlineData("```csharp\" onclick=\"bad\ntext\n```")]
    [InlineData("Внутри строки ``` не открывают блок")]
    public void Format_UnrecognizedOrEmptyCode_IsSentAsOriginalPlainText(string source)
    {
        // Arrange

        // Act
        var messages = Formatter().Format(RequestChecks.LlmText(source));

        // Assert
        Assert.All(messages, message => Assert.Equal(TelegramTextFormat.Plain, message.Format));
        Assert.Equal(source, string.Concat(messages.Select(message => message.Text)));
    }

    [Fact]
    public void Format_UnsafeLanguage_CannotBeUsedAsAnHtmlAttribute()
    {
        // Arrange

        // Act
        Action act = () => TelegramChecks.CodeLanguage("x\" onclick=\"bad");

        // Assert
        Assert.Throws<InputValidationException>(act);
        Assert.Throws<InputValidationException>(() => TelegramChecks.CodeLanguage(new string('x', 41)));
    }

    private TelegramReplyFormatter Formatter()
    {
        return new TelegramReplyFormatter();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(12288)]
    public void SplitText_TextAroundTelegramLimit_PreservesTextAndRespectsLimit(int length)
    {
        // Arrange
        var text = new string('x', length);

        // Act
        var parts = new TelegramReplyFormatter().SplitText(RequestChecks.LlmText(text));

        // Assert
        Assert.Equal(text, string.Concat(parts.Select(part => part)));
        Assert.All(parts, part => Assert.InRange(part.Length, 1, 4096));
        Assert.Equal((length + 4095) / 4096, parts.Count);
    }

    [Fact]
    public void Format_SurrogatePairs_AreNotSplitAtBoundary()
    {
        // Arrange
        var text = new string('x', 4095) + "😀" + new string('я', 5000) + "👩‍💻";

        // Act
        var parts = new TelegramReplyFormatter().SplitText(RequestChecks.LlmText(text));

        // Assert
        Assert.Equal(text, string.Concat(parts.Select(part => part)));
        Assert.All(parts, part =>
        {
            Assert.False(char.IsHighSurrogate(part[^1]));
            Assert.False(char.IsLowSurrogate(part[0]));
        });
    }

    [Fact]
    public void Format_WhitespaceAndMarkup_AreNotTrimmedOrEscaped()
    {
        // Arrange
        var text = "<b>Пример</b>\n```csharp\n" + new string(' ', 8192) + "\n```  ";

        // Act
        var parts = new TelegramReplyFormatter().SplitText(RequestChecks.LlmText(text));

        // Assert
        Assert.Equal(text, string.Concat(parts.Select(part => part)));
        Assert.Contains(parts, part => string.IsNullOrWhiteSpace(part));
    }

}
