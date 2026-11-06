using ItmoBot.Infrastructure.Logging;
using ItmoBot.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ItmoBot.Tests.Unit.Logging;

public sealed class LoggingTests
{
    [Fact]
    public void WriteLog_ExceptionPayload_IsNotWrittenToConsoleOrPassedToStateFormatter()
    {
        // Arrange
        var formatter = new SecretConsoleFormatter(new SecretRedactor(new TestValues().Settings()));
        using var output = new StringWriter();
        var exception = new InvalidOperationException("chat_id=424242 user-name private-dialogue private-system-prompt private-signature");
        var entry = new LogEntry<string>(LogLevel.Error, "test", new EventId(1), "Безопасное описание ошибки", exception,
            (state, error) => error is null ? state : state + error.Message);

        // Act
        formatter.Write(in entry, null, output);

        // Assert
        var text = output.ToString();
        Assert.Contains("Безопасное описание ошибки", text);
        Assert.Contains(nameof(InvalidOperationException), text);
        Assert.DoesNotContain("424242", text);
        Assert.DoesNotContain("user-name", text);
        Assert.DoesNotContain("private-dialogue", text);
        Assert.DoesNotContain("private-system-prompt", text);
        Assert.DoesNotContain("private-signature", text);
    }

    [Fact]
    public void Redact_ConfiguredSecretsAndCredentials_HidesAllSecrets()
    {
        // Arrange
        var settings = new TestValues().Settings(new Dictionary<string, string>
        {
            ["TELEGRAM_PROXY_URL"] = "http://student:p%40ss@localhost:8080",
        });

        // Act
        var text = new SecretRedactor(settings).Redact(
            $"Ошибка {new Exception(TestValues.Token + " db-secret p@ss test-only-openrouter-secret")} http://user:other-password@host:123");
        foreach (var secret in new[] { TestValues.Token, "db-secret", "p@ss", "other-password", "test-only-openrouter-secret" })
        {

            // Assert
            Assert.DoesNotContain(secret, text);
        }

        Assert.Contains("Exception", text);
    }

}
