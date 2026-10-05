using ItmoBot.Infrastructure.Logging;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Unit.Logging;

public sealed class SecretRedactorTests
{
    [Fact]
    public void RedactsConfiguredSecretsDecodedCredentialsAndExceptions()
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
