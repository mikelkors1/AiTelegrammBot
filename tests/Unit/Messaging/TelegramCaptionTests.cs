using ItmoBot.Application.Commands;
using ItmoBot.Application.Conversations;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Shared.Validation;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Messaging;

public sealed class TelegramCaptionTests
{
    [Theory]
    [InlineData(AssistantMode.Study)]
    [InlineData(AssistantMode.Translate)]
    [InlineData(AssistantMode.Quiz)]
    public void CreateCaption_FullWelcome_FitsPhotoCaptionInEveryMode(AssistantMode mode)
    {
        // Arrange
        var formatter = new CommandReplyFormatter(RequestChecks.ModelName("test"));
        var state = new ConversationState(AgentChecks.ChatId(42), mode, RequestChecks.Temperature(1.0m), RequestChecks.ConversationRevision(0));
        string welcome = formatter.Welcome(state);

        // Act
        var actualResult = TelegramChecks.TelegramCaption(welcome);

        // Assert
        Assert.Equal(welcome, actualResult);
    }

    [Fact]
    public void CreateCaption_Caption_EnforcesPhotoLimitInsteadOfTextMessageLimit()
    {
        // Arrange

        // Act
        var actualResult = TelegramChecks.TelegramCaption(new string('a', 1024)).Length;

        // Assert
        Assert.Equal(1024, actualResult);
        Assert.Throws<InputValidationException>(() => TelegramChecks.TelegramCaption(new string('a', 1025)));
        Assert.Throws<InputValidationException>(() => TelegramChecks.TelegramCaption(""));
    }
}
