using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;
using Xunit;

namespace ItmoBot.Tests.Unit.Llm;

public sealed class LlmRequestTests
{
    [Fact]
    public void MutatingSourceHistoryDoesNotChangeAnAlreadyPreparedRequest()
    {
        // Arrange
        var original = new LlmMessage(LlmMessageRole.User, new LlmText("Первый запрос"));
        var history = new List<LlmMessage> { original };
        var request = new LlmRequest(history, Temperature.Zero);

        history.Clear();

        // Act
        history.Add(new LlmMessage(LlmMessageRole.User, new LlmText("Другой запрос")));

        // Assert
        Assert.Equal(original, Assert.Single(request.Messages));
        Assert.Throws<ArgumentException>(() => new LlmRequest([], Temperature.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmMessage((LlmMessageRole)99, original.Content));
    }
}
