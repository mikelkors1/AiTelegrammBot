using System.Xml.Linq;
using ItmoBot.Application.Messaging;
using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Exceptions;
using Xunit;

namespace ItmoBot.Tests.Unit.Messaging;

public sealed class TelegramReplyFormatterTests
{
    [Fact]
    public void FencedCodeBecomesPreformattedHtmlWithLanguage()
    {
        // Arrange
        var source = "Пример:\n```csharp\nConsole.WriteLine(\"Привет\");\n```\nКонец.";

        // Act
        var actualResult = Formatter().Format(new LlmText(source));

        // Assert
        var message = Assert.Single(actualResult);

        Assert.Equal(TelegramTextFormat.Html, message.Format);
        var html = XDocument.Parse("<root>" + message.Text.Value + "</root>");
        Assert.Equal("language-csharp", html.Descendants("code").Single().Attribute("class")!.Value);
        Assert.Equal("Console.WriteLine(\"Привет\");\n", html.Descendants("code").Single().Value);
        Assert.Equal("Пример:\nConsole.WriteLine(\"Привет\");\n\nКонец.", html.Root!.Value);
    }

    [Fact]
    public void HtmlFromModelIsEscapedBothInsideAndOutsideCode()
    {
        // Arrange
        var source = "<b>Текст & тест</b>\n```html\n</code></pre><a href=\"tg://user?id=1\">x</a>\n```";

        // Act
        var actualResult = Formatter().Format(new LlmText(source));

        // Assert
        var message = Assert.Single(actualResult);
        var html = XDocument.Parse("<root>" + message.Text.Value + "</root>");

        Assert.Empty(html.Descendants("b"));
        Assert.Empty(html.Descendants("a"));
        Assert.Contains("<b>Текст & тест</b>", html.Root!.Value);
        Assert.Equal("</code></pre><a href=\"tg://user?id=1\">x</a>\n", html.Descendants("code").Single().Value);
    }

    [Fact]
    public void LongCodeIsSplitIntoIndependentlyValidBlocksWithoutLosingContent()
    {
        // Arrange
        var code = string.Concat(Enumerable.Repeat("var x = \"😀<&>\";\n", 1000));

        // Act
        var messages = Formatter().Format(new LlmText("```csharp\n" + code + "```"));

        // Assert
        Assert.True(messages.Count > 1);
        var restored = new List<string>();
        foreach (var message in messages)
        {
            Assert.InRange(message.Text.Value.Length, 1, 4096);
            Assert.Equal(TelegramTextFormat.Html, message.Format);
            var html = XDocument.Parse("<root>" + message.Text.Value + "</root>");
            Assert.All(html.Descendants("code"), element => Assert.Equal("language-csharp", element.Attribute("class")!.Value));
            restored.Add(html.Root!.Value);
        }

        Assert.Equal(code, string.Concat(restored));
    }

    [Fact]
    public void MultipleLanguagesAndAnonymousCodeAreHandled()
    {
        // Arrange
        var source = "```cs\nint x = 1;\n```\n```python\nprint(1)\n```\n```\nx\n```";

        // Act
        var actualResult = Formatter().Format(new LlmText(source));

        // Assert
        var message = Assert.Single(actualResult);
        var blocks = XDocument.Parse("<root>" + message.Text.Value + "</root>").Descendants("code").ToArray();
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
    public void UnrecognizedOrEmptyCodeIsSentAsOriginalPlainText(string source)
    {
        // Arrange

        // Act
        var messages = Formatter().Format(new LlmText(source));

        // Assert
        Assert.All(messages, message => Assert.Equal(TelegramTextFormat.Plain, message.Format));
        Assert.Equal(source, string.Concat(messages.Select(message => message.Text.Value)));
    }

    [Fact]
    public void UnsafeLanguageCannotBeUsedAsAnHtmlAttribute()
    {
        // Arrange

        // Act
        Action act = () => new CodeLanguage("x\" onclick=\"bad");

        // Assert
        Assert.Throws<ValueObjectValidationException>(act);
        Assert.Throws<ValueObjectValidationException>(() => new CodeLanguage(new string('x', 41)));
    }

    private TelegramReplyFormatter Formatter()
    {
        return new TelegramReplyFormatter(new CodeBlockParser(), new HtmlSegmentRenderer(), new TelegramTextSplitter());
    }
}
