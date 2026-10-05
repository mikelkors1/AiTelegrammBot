using ItmoBot.Application.ValueObjects;
using ItmoBot.Application.Models;
using ItmoBot.Application.Commands;
using ItmoBot.Shared.Exceptions;
using System.Net;
using System.Text.Json;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Infrastructure.Telegram;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Telegram;

public sealed class TelegramGatewayTests
{
    [Fact]
    public async Task PhotoIsUploadedAsMultipartAndReturnsMessageId()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport), new TelegramErrorMapper());
        using var photo = new MemoryStream(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        // Act
        var actualResult = await gateway.SendPhotoAsync(new ChatId(42), photo, CancellationToken.None,
            new TelegramCaption("Привет! <b>обычный текст</b>"), new BotMenu().GetKeyboard(BotMenuPage.Main));

        // Assert
        var result = Assert.IsType<TelegramSendResult.Success>(actualResult);
        Assert.Equal(new MessageId(1), result.MessageId);
        Assert.Equal("sendPhoto", Assert.Single(transport.Methods));
        Assert.Equal("multipart/form-data", transport.ContentType);
        Assert.Contains("nerdy-ai-welcome.png", transport.Body!);
        Assert.Contains("Привет! <b>обычный текст</b>", transport.Body!);
        Assert.Contains("reply_markup", transport.Body!);
        Assert.Contains("resize_keyboard", transport.Body!);
        Assert.DoesNotContain("parse_mode", transport.Body!);
    }

    [Fact]
    public async Task PhotoUploadFailureIsSafeAndCallerCancellationPropagates()
    {
        // Arrange
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(new TelegramTransport
        {
            Status = HttpStatusCode.Forbidden,
        }), new TelegramErrorMapper());
        using var photo = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var actualResult = await gateway.SendPhotoAsync(new ChatId(42), photo, CancellationToken.None);

        // Assert
        var failure = Assert.IsType<TelegramSendResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.TelegramForbidden, failure.Error.Code);
        Assert.DoesNotContain("secret-error", failure.Message);
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.SendPhotoAsync(new ChatId(42), photo, stopping.Token));
    }

    [Fact]
    public async Task SendReturnsMessageIdAndMapsPersistentReplyKeyboard()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport), new TelegramErrorMapper());
        var menu = new BotMenu();

        // Act
        var actualResult = await gateway.SendTextAsync(new ChatId(42),
            new TelegramText("Меню"), CancellationToken.None, keyboard: menu.GetKeyboard(BotMenuPage.Main));

        // Assert
        var result = Assert.IsType<TelegramSendResult.Success>(actualResult);

        Assert.Equal(new MessageId(1), result.MessageId);
        using var json = JsonDocument.Parse(transport.Body!);
        var markup = json.RootElement.GetProperty("reply_markup");
        Assert.True(markup.GetProperty("resize_keyboard").GetBoolean());
        Assert.True(markup.GetProperty("is_persistent").GetBoolean());
        var labels = markup.GetProperty("keyboard").EnumerateArray().SelectMany(row =>
            row.EnumerateArray().Select(button => button.GetProperty("text").GetString())).ToArray();
        Assert.Equal(menu.GetKeyboard(BotMenuPage.Main).Rows.SelectMany(row => row).Select(label => label.Value), labels);
    }

    [Fact]
    public async Task DeletionTargetsExactChatAndMessage()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport), new TelegramErrorMapper());

        // Act
        var actualResult = await gateway.DeleteMessageAsync(new ChatId(42), new MessageId(17), CancellationToken.None);

        // Assert
        Assert.IsType<TelegramDeleteResult.Success>(actualResult);

        Assert.Equal("deleteMessage", Assert.Single(transport.Methods));
        using var json = JsonDocument.Parse(transport.Body!);
        Assert.Equal(42, json.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal(17, json.RootElement.GetProperty("message_id").GetInt32());
    }

    [Fact]
    public async Task DeletionFailureIsSafeAndCallerCancellationPropagates()
    {
        // Arrange
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(new TelegramTransport
        {
            Status = HttpStatusCode.Forbidden,
        }), new TelegramErrorMapper());

        // Act
        var actualResult = await gateway.DeleteMessageAsync(new ChatId(42), new MessageId(17), CancellationToken.None);

        // Assert
        var failure = Assert.IsType<TelegramDeleteResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.TelegramForbidden, failure.Error.Code);
        Assert.DoesNotContain("secret-error", failure.Error.Message);
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.DeleteMessageAsync(new ChatId(42), new MessageId(17), stopping.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MessageIdRejectsInvalidValues(int value)
    {
        // Arrange

        // Act
        Action act = () => new MessageId(value);

        // Assert
        Assert.Throws<ValueObjectValidationException>(act);
    }

    [Fact]
    public async Task ExistingWebhookReturnsFailureWithoutDeletingIt()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport), new TelegramErrorMapper());

        // Act
        var actualResult = await gateway.InitializeAsync(CancellationToken.None);

        // Assert
        var failure = Assert.IsType<TelegramInitializationResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.WebhookConfigured, failure.Error.Code);
        Assert.Equal(new[] { "getMe", "getWebhookInfo" }, transport.Methods);
    }

    [Fact]
    public async Task NoWebhookReturnsUsername()
    {
        // Arrange
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(new TelegramTransport { WebhookUrl = "" }), new TelegramErrorMapper());

        // Act
        var actualResult = Assert.IsType<TelegramInitializationResult.Success>(await gateway.InitializeAsync(CancellationToken.None)).Username.Value;

        // Assert
        Assert.Equal("test_bot", actualResult);
    }

    [Fact]
    public async Task PollingMapsSdkUpdatesToApplicationModels()
    {
        // Arrange
        var transport = new TelegramTransport
        {
            UpdatesJson = """
                [
                  {"update_id":17,"message":{"message_id":1,"date":0,"chat":{"id":42,"type":"private"},"text":"Привет"}},
                  {"update_id":18,"message":{"message_id":2,"date":0,"chat":{"id":42,"type":"private"},"caption":"Фото"}},
                  {"update_id":19}
                ]
                """,
        };
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport), new TelegramErrorMapper());

        // Act
        var actualResult = await gateway.GetUpdatesAsync(new UpdateOffset(10), CancellationToken.None);

        // Assert
        var result = Assert.IsType<TelegramPollingResult.Success>(actualResult);
        Assert.Equal(new IncomingUpdate(new UpdateId(17), new ChatId(42), new TelegramText("Привет")), result.Updates[0]);
        Assert.Equal(new IncomingUpdate(new UpdateId(18), new ChatId(42), null), result.Updates[1]);
        Assert.Equal(new IncomingUpdate(new UpdateId(19), null, null), result.Updates[2]);
        using var body = JsonDocument.Parse(transport.Body!);
        Assert.Equal(10, body.RootElement.GetProperty("offset").GetInt32());
    }

    [Fact]
    public async Task GroupMessagesAreIgnoredBeforeConstructingMessageValues()
    {
        // Arrange
        var transport = new TelegramTransport
        {
            UpdatesJson = "[{\"update_id\":17,\"message\":{\"message_id\":1,\"date\":0,\"chat\":{\"id\":-42,\"type\":\"group\"},\"text\":\"\"}}]",
        };
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport), new TelegramErrorMapper());

        // Act
        var actualResult = await gateway.GetUpdatesAsync(UpdateOffset.Zero, CancellationToken.None);

        // Assert
        var result = Assert.IsType<TelegramPollingResult.Success>(actualResult);

        var update = Assert.Single(result.Updates);
        Assert.Equal(new UpdateId(17), update.Id);
        Assert.Null(update.ChatId);
        Assert.Null(update.Text);
    }

    [Theory]
    [InlineData(-1, 42, "Привет", ErrorCode.InvalidUpdateId)]
    [InlineData(17, 0, "Привет", ErrorCode.InvalidChatId)]
    [InlineData(17, 42, "", ErrorCode.InvalidMessageText)]
    public async Task InvalidIncomingValuesReturnSafeFailure(int id, long chatId, string text, ErrorCode expectedCode)
    {
        // Arrange
        var transport = new TelegramTransport
        {
            UpdatesJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    update_id = id,
                    message = new
                    {
                        message_id = 1,
                        date = 0,
                        chat = new { id = chatId, type = "private" },
                        text,
                    },
                },
            }),
        };
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport), new TelegramErrorMapper());

        // Act
        var result = await gateway.GetUpdatesAsync(UpdateOffset.Zero, CancellationToken.None);

        // Assert
        var failure = Assert.IsType<TelegramPollingResult.Failure>(result);
        Assert.Equal(expectedCode, failure.Error.Code);
        Assert.DoesNotContain(text == "" ? "secret" : text, failure.Message);
        Assert.False(failure.Error.IsRetryable);
    }

    [Fact]
    public async Task OversizedIncomingTextReturnsFailureAtAdapterBoundary()
    {
        // Arrange
        var text = new string('a', 4097);
        var transport = new TelegramTransport
        {
            UpdatesJson = "[{\"update_id\":17,\"message\":{\"message_id\":1,\"date\":0,\"chat\":{\"id\":42,\"type\":\"private\"},\"text\":"
                + JsonSerializer.Serialize(text) + "}}]",
        };
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport), new TelegramErrorMapper());

        // Act
        var actualResult = await gateway.GetUpdatesAsync(UpdateOffset.Zero, CancellationToken.None);

        // Assert
        var failure = Assert.IsType<TelegramPollingResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.InvalidMessageText, failure.Error.Code);
        Assert.DoesNotContain(text, failure.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ErrorCode.TelegramUnauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, ErrorCode.TelegramForbidden, false)]
    [InlineData(HttpStatusCode.Conflict, ErrorCode.TelegramPollingConflict, false)]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorCode.TelegramRateLimited, true)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorCode.TelegramUnavailable, true)]
    public async Task ApiFailuresAreClassifiedAndDoNotLeakDescriptions(HttpStatusCode status, ErrorCode code, bool retryable)
    {
        // Arrange
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(new TelegramTransport { Status = status }), new TelegramErrorMapper());

        // Act
        var actualResult = await gateway.GetUpdatesAsync(UpdateOffset.Zero, CancellationToken.None);

        // Assert
        var failure = Assert.IsType<TelegramPollingResult.Failure>(actualResult);
        Assert.Equal(code, failure.Error.Code);
        Assert.Equal(retryable, failure.Error.IsRetryable);
        Assert.DoesNotContain("secret-error", failure.Message);
    }

    [Fact]
    public async Task SendPreservesTextAndDoesNotEnableParseMode()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport), new TelegramErrorMapper());

        // Act
        var actualResult = await gateway.SendTextAsync(new TestValues().Chat(42), new TestValues().Text("<b>текст</b>"), CancellationToken.None);

        // Assert
        Assert.IsType<TelegramSendResult.Success>(actualResult);
        using var json = JsonDocument.Parse(transport.Body!);
        Assert.Equal("<b>текст</b>", json.RootElement.GetProperty("text").GetString());
        Assert.Equal(42, json.RootElement.GetProperty("chat_id").GetInt64());
        Assert.False(json.RootElement.TryGetProperty("parse_mode", out _));
    }

    [Fact]
    public async Task FormattedSendExplicitlyEnablesHtmlParseMode()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport), new TelegramErrorMapper());

        // Act
        var actualResult = await gateway.SendTextAsync(new ChatId(42),
            new TelegramText("<pre><code>int x = 1;</code></pre>"), CancellationToken.None, TelegramTextFormat.Html);

        // Assert
        Assert.IsType<TelegramSendResult.Success>(actualResult);
        using var body = JsonDocument.Parse(transport.Body!);
        Assert.Equal("Html", body.RootElement.GetProperty("parse_mode").GetString());
        Assert.Equal("<pre><code>int x = 1;</code></pre>", body.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task NetworkExceptionReturnsSafeRetryableFailure()
    {
        // Arrange
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(new TelegramTransport
        {
            Error = new HttpRequestException("secret-network-error"),
        }), new TelegramErrorMapper());

        // Act
        var actualResult = await gateway.SendTextAsync(
            new TestValues().Chat(42), new TestValues().Text("Привет"), CancellationToken.None);

        // Assert
        var failure = Assert.IsType<TelegramSendResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.TelegramUnavailable, failure.Error.Code);
        Assert.True(failure.Error.IsRetryable);
        Assert.DoesNotContain("secret-network-error", failure.Message);
    }

    [Fact]
    public async Task TimeoutReturnsRetryableErrorButCallerCancellationPropagates()
    {
        // Arrange
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(new TelegramTransport
        {
            Error = new OperationCanceledException("secret-error"),
        }), new TelegramErrorMapper());

        // Act
        var actualResult = await gateway.InitializeAsync(CancellationToken.None);

        // Assert
        var failure = Assert.IsType<TelegramInitializationResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.TelegramTimeout, failure.Error.Code);
        Assert.True(failure.Error.IsRetryable);
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.GetUpdatesAsync(UpdateOffset.Zero, stopping.Token));
    }
}
