using ItmoBot.Configuration.Secrets;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Configuration;

public sealed class InputValidationTests
{

    [Theory]
    [InlineData("-0.1")]
    [InlineData("0.5")]
    [InlineData("2.0")]
    public void Validate_Creativity_RejectsUnsupportedValues(string value)
    {
        // Arrange
        var numeric = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        // Act
        Action act = () => RequestChecks.Temperature(numeric);

        // Assert
        var error = Assert.Throws<InputValidationException>(act);
        Assert.Equal(ErrorCode.InvalidTemperature, error.Code);
    }

    [Fact]
    public void Validate_LlmText_HasNoTelegramLengthLimitAndRejectsWhitespace()
    {
        // Arrange
        var text = " " + new string('а', 5000) + "\n";

        // Act
        var actualResult = RequestChecks.LlmText(text);

        // Assert
        Assert.Equal(text, actualResult);
        Assert.Throws<InputValidationException>(() => RequestChecks.LlmText(" \n"));
        Assert.Throws<InputValidationException>(() => RequestChecks.LlmText(null!));
    }

    [Theory]
    [InlineData("https://openrouter.ai/api/v1")]
    [InlineData("http://127.0.0.1:8081/api/v1")]
    public void Validate_ApiAddress_NormalizesTrailingSlash(string value)
    {
        // Arrange

        // Act
        var actualResult = ConfigurationChecks.LlmApiAddress(value).AbsoluteUri;

        // Assert
        Assert.Equal(value + "/", actualResult);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Validate_UpdateId_RejectsUnusableValues(int value)
    {
        // Arrange

        // Act
        Action act = () => AgentChecks.UpdateId(value);

        // Assert
        var error = Assert.Throws<InputValidationException>(act);
        Assert.Equal(ErrorCode.InvalidUpdateId, error.Code);
    }

    [Fact]
    public void Validate_PollingOffset_SupportsZeroAndRejectsNegativeValues()
    {
        // Arrange

        // Act
        int actualResult = AgentChecks.UpdateOffset(0);

        // Assert
        Assert.Equal(0, actualResult);
        Assert.Equal(int.MaxValue, AgentChecks.UpdateOffset(int.MaxValue));
        var error = Assert.Throws<InputValidationException>(() => AgentChecks.UpdateOffset(-1));
        Assert.Equal(ErrorCode.InvalidUpdateOffset, error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("secret\nvalue")]
    public void Validate_DatabaseValuesAndBotUsername_RejectEmptyOrControlCharacters(string? value)
    {
        // Arrange

        // Act
        Action act = () => ConfigurationChecks.PostgresHost(value!);

        // Assert
        var hostError = Assert.Throws<InputValidationException>(act);
        var nameError = Assert.Throws<InputValidationException>(() => ConfigurationChecks.DatabaseName(value!));
        var userError = Assert.Throws<InputValidationException>(() => ConfigurationChecks.DatabaseUser(value!));
        var botError = Assert.Throws<InputValidationException>(() => TelegramChecks.BotUsername(value!));
        Assert.Contains("POSTGRES_HOST", hostError.Message);
        Assert.Contains("POSTGRES_DB", nameError.Message);
        Assert.Contains("POSTGRES_USER", userError.Message);
        Assert.Equal(ErrorCode.InvalidBotUsername, botError.Code);
        Assert.DoesNotContain("secret", hostError.Message + nameError.Message + userError.Message + botError.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void Validate_Port_AcceptsBoundaries(int value)
    {
        // Arrange

        // Act
        var actualResult = ConfigurationChecks.PortNumber(value);

        // Assert
        Assert.Equal(value, actualResult);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_Port_RejectsValuesOutsideRange(int value)
    {
        // Arrange

        // Act
        Action act = () => ConfigurationChecks.PortNumber(value);

        // Assert
        var error = Assert.Throws<InputValidationException>(act);
        Assert.Equal(ErrorCode.InvalidPort, error.Code);
    }

    [Theory]
    [InlineData(-100123456L)]
    [InlineData(42L)]
    public void Validate_ChatId_SupportsGroupsAndPrivateChats(long value)
    {
        // Arrange

        // Act
        var actualResult = AgentChecks.ChatId(value);

        // Assert
        Assert.Equal(value, actualResult);
        Assert.Throws<InputValidationException>(() => AgentChecks.ChatId(0));
    }

    [Fact]
    public void Validate_Text_PreservesWhitespaceAndEnforcesTelegramLimit()
    {
        // Arrange

        // Act
        var actualResult = TelegramChecks.TelegramText(" \n");

        // Assert
        Assert.Equal(" \n", actualResult);
        Assert.Equal(4096, TelegramChecks.TelegramText(new string('a', 4096)).Length);
        Assert.Throws<InputValidationException>(() => TelegramChecks.TelegramText(new string('a', 4097)));
        Assert.Throws<InputValidationException>(() => TelegramChecks.TelegramText(""));
        Assert.Throws<InputValidationException>(() => TelegramChecks.TelegramText(null!));
    }

    [Theory]
    [InlineData("http://student:p%40ss@localhost:8080")]
    [InlineData("http://localhost:80")]
    [InlineData("socks5://localhost:1080")]
    public void Validate_Proxy_AcceptsSupportedExplicitPorts(string value)
    {
        // Arrange

        // Act
        var proxy = new ProxyAddress(value);

        // Assert
        Assert.Equal(value, proxy.Value.OriginalString);
        Assert.Equal("[скрыто]", proxy.ToString());
    }

    [Fact]
    public void Validate_Secrets_DoNotRevealValues()
    {
        // Arrange
        var token = new TelegramBotToken(TestValues.Token);

        // Act
        var password = new SecretValue("secret-password");

        // Assert
        Assert.DoesNotContain(TestValues.Token, token.ToString());
        Assert.DoesNotContain("secret-password", password.ToString());
    }

    [Fact]
    public void Create_InvalidSecretsAndProxy_ThrowsSafeArgumentExceptions()
    {
        // Arrange

        // Act
        Action act = () => new TelegramBotToken("invalid-secret");

        // Assert
        var tokenError = Assert.Throws<InputValidationException>(act);
        var proxyError = Assert.Throws<InputValidationException>(() => new ProxyAddress("ftp://user:invalid-secret@host:123"));
        var passwordError = Assert.Throws<InputValidationException>(() => new SecretValue(""));
        Assert.DoesNotContain("invalid-secret", tokenError.Message);
        Assert.DoesNotContain("invalid-secret", proxyError.Message);
        Assert.Equal(ErrorCode.InvalidConfiguration, passwordError.Code);
    }
}
