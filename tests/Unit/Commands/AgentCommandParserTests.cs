using ItmoBot.Application.Commands;
using ItmoBot.Application.Reminders;
using ItmoBot.Shared.Errors;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Commands;

public sealed class AgentCommandParserTests
{
    [Theory]
    [InlineData("/tools", typeof(BotCommand.ShowTools))]
    [InlineData("/why", typeof(BotCommand.ShowWhy))]
    [InlineData("/timezone", typeof(BotCommand.ShowTimeZone))]
    [InlineData("/timezone Europe/Moscow", typeof(BotCommand.SetTimeZone))]
    [InlineData("🕒 Часовой пояс", typeof(BotCommand.ShowTimeZone))]
    [InlineData("🛠 Инструменты", typeof(BotCommand.ShowTools))]
    [InlineData("🔎 Почему?", typeof(BotCommand.ShowWhy))]
    public void Parse_AgentCommandsAndMenuButtons_AreRecognized(string text, Type expected)
    {
        // Arrange
        var parser = new BotCommandParser(new BotMenu());

        // Act
        var result = parser.Parse(TelegramChecks.TelegramText(text));

        // Assert
        Assert.IsType(expected, Assert.IsType<BotCommandParseResult.Success>(result).Command);
    }

    [Theory]
    [InlineData("/timezone Invalid/City", ErrorCode.InvalidTimeZone)]
    [InlineData("/timezone Europe/Moscow extra", ErrorCode.InvalidTimeZone)]
    [InlineData("/tools secret", ErrorCode.InvalidBotCommandArgument)]
    [InlineData("/why 84", ErrorCode.InvalidBotCommandArgument)]
    public void Parse_InvalidZoneAndOwnerArguments_AreRejected(string text, ErrorCode expected)
    {
        // Arrange
        var parser = new BotCommandParser(new BotMenu());

        // Act
        var result = parser.Parse(TelegramChecks.TelegramText(text));

        // Assert
        Assert.Equal(expected, Assert.IsType<BotCommandParseResult.Failure>(result).Error.Code);
    }

    [Theory]
    [InlineData("r:y:not-an-id")]
    [InlineData("r:z:33333333333333333333333333333333")]
    [InlineData("r:y:00000000000000000000000000000000")]
    [InlineData("r:y:33333333333333333333333333333333:owner:84")]
    public void Parse_MalformedCallbackData_DoesNotSelectAction(string text)
    {
        // Arrange
        var codec = new ReminderCallbackCodec();

        // Act
        var decoded = codec.Decode(TelegramChecks.CallbackData(text));

        // Assert
        Assert.Null(decoded);
    }
}
