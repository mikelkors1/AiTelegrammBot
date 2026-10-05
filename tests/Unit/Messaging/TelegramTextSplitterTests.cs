using ItmoBot.Application.Messaging;
using ItmoBot.Application.ValueObjects;
using Xunit;

namespace ItmoBot.Tests.Unit.Messaging;

public sealed class TelegramTextSplitterTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(12288)]
    public void SplitsAtLimitAndPreservesExactText(int length)
    {
        // Arrange
        var text = new string('x', length);

        // Act
        var parts = new TelegramTextSplitter().Split(new LlmText(text));

        // Assert
        Assert.Equal(text, string.Concat(parts.Select(part => part.Value)));
        Assert.All(parts, part => Assert.InRange(part.Value.Length, 1, 4096));
        Assert.Equal((length + 4095) / 4096, parts.Count);
    }

    [Fact]
    public void SurrogatePairsAreNotSplitAtBoundary()
    {
        // Arrange
        var text = new string('x', 4095) + "😀" + new string('я', 5000) + "👩‍💻";

        // Act
        var parts = new TelegramTextSplitter().Split(new LlmText(text));

        // Assert
        Assert.Equal(text, string.Concat(parts.Select(part => part.Value)));
        Assert.All(parts, part =>
        {
            Assert.False(char.IsHighSurrogate(part.Value[^1]));
            Assert.False(char.IsLowSurrogate(part.Value[0]));
        });
    }

    [Fact]
    public void WhitespaceAndMarkupAreNotTrimmedOrEscaped()
    {
        // Arrange
        var text = "<b>Пример</b>\n```csharp\n" + new string(' ', 8192) + "\n```  ";

        // Act
        var parts = new TelegramTextSplitter().Split(new LlmText(text));

        // Assert
        Assert.Equal(text, string.Concat(parts.Select(part => part.Value)));
        Assert.Contains(parts, part => string.IsNullOrWhiteSpace(part.Value));
    }
}
