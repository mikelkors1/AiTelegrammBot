using ItmoBot.Application.Commands;
using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Exceptions;
using Xunit;

namespace ItmoBot.Tests.Unit.Messaging;

public sealed class TelegramCaptionTests
{
    [Theory]
    [InlineData(AssistantMode.Study)]
    [InlineData(AssistantMode.Translate)]
    [InlineData(AssistantMode.Quiz)]
    public void FullWelcomeFitsPhotoCaptionInEveryMode(AssistantMode mode)
    {
        // Arrange
        var formatter = new CommandReplyFormatter(new ModelName("test"));
        var state = new ConversationState(new ChatId(42), mode, new Temperature(1.0m), ConversationRevision.Zero);
        var welcome = formatter.Welcome(state);

        // Act
        var actualResult = new TelegramCaption(welcome.Value).Value;

        // Assert
        Assert.Equal(welcome.Value, actualResult);
    }

    [Fact]
    public void CaptionEnforcesPhotoLimitInsteadOfTextMessageLimit()
    {
        // Arrange

        // Act
        var actualResult = new TelegramCaption(new string('a', 1024)).Value.Length;

        // Assert
        Assert.Equal(1024, actualResult);
        Assert.Throws<ValueObjectValidationException>(() => new TelegramCaption(new string('a', 1025)));
        Assert.Throws<ValueObjectValidationException>(() => new TelegramCaption(""));
    }
}
