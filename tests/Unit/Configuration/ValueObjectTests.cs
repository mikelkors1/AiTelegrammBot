using ItmoBot.Application.ValueObjects;
using ItmoBot.Configuration.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Shared.ValueObjects;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Unit.Configuration;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("0.0")]
    [InlineData("0.3")]
    [InlineData("0.7")]
    [InlineData("1.0")]
    public void CreativityAcceptsOnlySupportedTemperatureValues(string value)
    {
        // Arrange
        var numeric = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        // Act
        var actualResult = new Temperature(numeric).ToString();

        // Assert
        Assert.Equal(value, actualResult);
        Assert.Equal(new Temperature(0m), Temperature.Zero);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("0.5")]
    [InlineData("2.0")]
    public void CreativityRejectsUnsupportedValues(string value)
    {
        // Arrange
        var numeric = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        // Act
        Action act = () => new Temperature(numeric);

        // Assert
        var error = Assert.Throws<ValueObjectValidationException>(act);
        Assert.Equal(ErrorCode.InvalidTemperature, error.Code);
    }

    [Fact]
    public void LlmTextHasNoTelegramLengthLimitAndRejectsWhitespace()
    {
        // Arrange
        var text = " " + new string('а', 5000) + "\n";

        // Act
        var actualResult = new LlmText(text).Value;

        // Assert
        Assert.Equal(text, actualResult);
        Assert.Throws<ValueObjectValidationException>(() => new LlmText(" \n"));
        Assert.Throws<ValueObjectValidationException>(() => new LlmText(null!));
        Assert.Equal(TokenCount.Zero, new TokenCount(0));
        Assert.Throws<ValueObjectValidationException>(() => new TokenCount(-1));
        Assert.Throws<ValueObjectValidationException>(() => new TokenLimit(0));
        Assert.Throws<ValueObjectValidationException>(() => new HistoryMessageLimit(0));
    }

    [Theory]
    [InlineData("https://openrouter.ai/api/v1")]
    [InlineData("http://127.0.0.1:8081/api/v1")]
    public void ApiAddressNormalizesTrailingSlash(string value)
    {
        // Arrange

        // Act
        var actualResult = new LlmApiAddress(value).Value.AbsoluteUri;

        // Assert
        Assert.Equal(value + "/", actualResult);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(int.MaxValue - 1)]
    public void UpdateIdProducesNextPollingOffsetWithoutOverflow(int value)
    {
        // Arrange

        // Act
        var id = new UpdateId(value);

        // Assert
        Assert.Equal(value + 1, id.NextOffset().Value);
        Assert.Equal(id, new UpdateId(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void UpdateIdRejectsUnusableValues(int value)
    {
        // Arrange

        // Act
        Action act = () => new UpdateId(value);

        // Assert
        var error = Assert.Throws<ValueObjectValidationException>(act);
        Assert.Equal(ErrorCode.InvalidUpdateId, error.Code);
    }

    [Fact]
    public void PollingOffsetSupportsZeroAndRejectsNegativeValues()
    {
        // Arrange

        // Act
        var actualResult = UpdateOffset.Zero;

        // Assert
        Assert.Equal(new UpdateOffset(0), actualResult);
        Assert.Equal(int.MaxValue, new UpdateOffset(int.MaxValue).Value);
        var error = Assert.Throws<ValueObjectValidationException>(() => new UpdateOffset(-1));
        Assert.Equal(ErrorCode.InvalidUpdateOffset, error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("secret\nvalue")]
    public void DatabaseValuesAndBotUsernameRejectEmptyOrControlCharacters(string? value)
    {
        // Arrange

        // Act
        Action act = () => new PostgresHost(value!);

        // Assert
        var hostError = Assert.Throws<ValueObjectValidationException>(act);
        var nameError = Assert.Throws<ValueObjectValidationException>(() => new DatabaseName(value!));
        var userError = Assert.Throws<ValueObjectValidationException>(() => new DatabaseUser(value!));
        var botError = Assert.Throws<ValueObjectValidationException>(() => new BotUsername(value!));
        Assert.Contains("POSTGRES_HOST", hostError.Message);
        Assert.Contains("POSTGRES_DB", nameError.Message);
        Assert.Contains("POSTGRES_USER", userError.Message);
        Assert.Equal(ErrorCode.InvalidBotUsername, botError.Code);
        Assert.DoesNotContain("secret", hostError.Message + nameError.Message + userError.Message + botError.Message);
    }

    [Fact]
    public void DatabaseValuesPreserveNamesAndValueEquality()
    {
        // Arrange

        // Act
        var actualResult = new PostgresHost("db.internal").Value;

        // Assert
        Assert.Equal("db.internal", actualResult);
        Assert.Equal(new DatabaseName("Учебная база"), new DatabaseName("Учебная база"));
        Assert.Equal("student", new DatabaseUser("student").Value);
        Assert.Equal("test_bot", new BotUsername("test_bot").Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void PortAcceptsBoundaries(int value)
    {
        // Arrange

        // Act
        var actualResult = new PortNumber(value).Value;

        // Assert
        Assert.Equal(value, actualResult);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void PortRejectsValuesOutsideRange(int value)
    {
        // Arrange

        // Act
        Action act = () => new PortNumber(value);

        // Assert
        var error = Assert.Throws<ValueObjectValidationException>(act);
        Assert.Equal(ErrorCode.InvalidPort, error.Code);
    }

    [Theory]
    [InlineData(-100123456L)]
    [InlineData(42L)]
    public void ChatIdSupportsGroupsAndPrivateChats(long value)
    {
        // Arrange

        // Act
        var actualResult = new ChatId(value).Value;

        // Assert
        Assert.Equal(value, actualResult);
        Assert.Throws<ValueObjectValidationException>(() => new ChatId(0));
    }

    [Fact]
    public void TextPreservesWhitespaceAndEnforcesTelegramLimit()
    {
        // Arrange

        // Act
        var actualResult = new TelegramText(" \n").Value;

        // Assert
        Assert.Equal(" \n", actualResult);
        Assert.Equal(4096, new TelegramText(new string('a', 4096)).Value.Length);
        Assert.Throws<ValueObjectValidationException>(() => new TelegramText(new string('a', 4097)));
        Assert.Throws<ValueObjectValidationException>(() => new TelegramText(""));
        Assert.Throws<ValueObjectValidationException>(() => new TelegramText(null!));
    }

    [Theory]
    [InlineData("http://student:p%40ss@localhost:8080")]
    [InlineData("http://localhost:80")]
    [InlineData("socks5://localhost:1080")]
    public void ProxyAcceptsSupportedExplicitPorts(string value)
    {
        // Arrange

        // Act
        var proxy = new ProxyAddress(value);

        // Assert
        Assert.Equal(value, proxy.Value.OriginalString);
        Assert.Equal("[скрыто]", proxy.ToString());
    }

    [Fact]
    public void SecretsPreserveEqualityAndDoNotRevealValues()
    {
        // Arrange
        var token = new TelegramBotToken(TestValues.Token);

        // Act
        var password = new SecretValue("secret-password");

        // Assert
        Assert.DoesNotContain(TestValues.Token, token.ToString());
        Assert.DoesNotContain("secret-password", password.ToString());
        Assert.Equal(token, new TelegramBotToken(TestValues.Token));
    }

    [Fact]
    public void InvalidSecretsAndProxyThrowSafeArgumentExceptions()
    {
        // Arrange

        // Act
        Action act = () => new TelegramBotToken("invalid-secret");

        // Assert
        var tokenError = Assert.Throws<ValueObjectValidationException>(act);
        var proxyError = Assert.Throws<ValueObjectValidationException>(() => new ProxyAddress("ftp://user:invalid-secret@host:123"));
        var passwordError = Assert.Throws<ValueObjectValidationException>(() => new SecretValue(""));
        Assert.IsAssignableFrom<ArgumentException>(tokenError);
        Assert.DoesNotContain("invalid-secret", tokenError.Message);
        Assert.DoesNotContain("invalid-secret", proxyError.Message);
        Assert.Equal(ErrorCode.InvalidConfiguration, passwordError.Code);
    }
}
