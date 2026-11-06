using System.Net;
using System.Text.Json;
using ItmoBot.Application.Commands;
using ItmoBot.Application.Messaging;
using ItmoBot.Infrastructure.Telegram;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Telegram;

public sealed class TelegramGatewayTests
{
    [Fact]
    public async Task CallTelegram_Photo_IsUploadedAsMultipartAndReturnsMessageId()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));
        using var photo = new MemoryStream(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        // Act
        Result<int> actualResult = await gateway.SendPhotoAsync(AgentChecks.ChatId(42), photo, CancellationToken.None,
            TelegramChecks.TelegramCaption("Привет! <b>обычный текст</b>"), new BotMenu().GetKeyboard(BotMenuPage.Main));

        // Assert
        Result<int>.Success result = Assert.IsType<Result<int>.Success>(actualResult);
        Assert.Equal(TelegramChecks.MessageId(1), result.Value);
        Assert.Equal("sendPhoto", Assert.Single(transport.Methods));
        Assert.Equal("multipart/form-data", transport.ContentType);
        Assert.Contains("nerdy-ai-welcome.png", transport.Body!);
        Assert.Contains("Привет! <b>обычный текст</b>", transport.Body!);
        Assert.Contains("reply_markup", transport.Body!);
        Assert.Contains("resize_keyboard", transport.Body!);
        Assert.DoesNotContain("parse_mode", transport.Body!);
    }


    [Fact]
    public async Task CallTelegram_Send_ReturnsMessageIdAndMapsPersistentReplyKeyboard()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));
        var menu = new BotMenu();

        // Act
        Result<int> actualResult = await gateway.SendTextAsync(AgentChecks.ChatId(42),
            TelegramChecks.TelegramText("Меню"), CancellationToken.None, keyboard: menu.GetKeyboard(BotMenuPage.Main));

        // Assert
        Result<int>.Success result = Assert.IsType<Result<int>.Success>(actualResult);

        Assert.Equal(TelegramChecks.MessageId(1), result.Value);
        using var json = JsonDocument.Parse(transport.Body!);
        var markup = json.RootElement.GetProperty("reply_markup");
        Assert.True(markup.GetProperty("resize_keyboard").GetBoolean());
        Assert.True(markup.GetProperty("is_persistent").GetBoolean());
        var labels = markup.GetProperty("keyboard").EnumerateArray().SelectMany(row =>
            row.EnumerateArray().Select(button => button.GetProperty("text").GetString())).ToArray();
        Assert.Equal(menu.GetKeyboard(BotMenuPage.Main).Rows.SelectMany(row => row).Select(label => label), labels);
    }

    [Fact]
    public async Task DeleteMessageAsync_SpecifiedChatAndMessage_SendsExactIdentifiers()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));

        // Act
        var actualResult = await gateway.DeleteMessageAsync(AgentChecks.ChatId(42), TelegramChecks.MessageId(17), CancellationToken.None);

        // Assert
        Assert.IsType<Result.Success>(actualResult);

        Assert.Equal("deleteMessage", Assert.Single(transport.Methods));
        using var json = JsonDocument.Parse(transport.Body!);
        Assert.Equal(42, json.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal(17, json.RootElement.GetProperty("message_id").GetInt32());
    }


    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CallTelegram_MessageId_RejectsInvalidValues(int value)
    {
        // Arrange

        // Act
        Action act = () => TelegramChecks.MessageId(value);

        // Assert
        Assert.Throws<InputValidationException>(act);
    }

    [Fact]
    public async Task CallTelegram_ExistingWebhook_ReturnsFailureWithoutDeletingIt()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));

        // Act
        Result<string> actualResult = await gateway.InitializeAsync(CancellationToken.None);

        // Assert
        Result<string>.Failure failure = Assert.IsType<Result<string>.Failure>(actualResult);
        Assert.Equal(ErrorCode.WebhookConfigured, failure.Error.Code);
        Assert.Equal(new[] { "getMe", "getWebhookInfo" }, transport.Methods);
    }

    [Fact]
    public async Task CallTelegram_NoWebhook_ReturnsUsername()
    {
        // Arrange
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(new TelegramTransport { WebhookUrl = "" }));

        // Act
        var actualResult = Assert.IsType<Result<string>.Success>(await gateway.InitializeAsync(CancellationToken.None)).Value;

        // Assert
        Assert.Equal("test_bot", actualResult);
    }

    [Fact]
    public async Task CallTelegram_Polling_MapsSdkUpdatesToApplicationModels()
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
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));

        // Act
        Result<IReadOnlyList<IncomingUpdate>> actualResult = await gateway.GetUpdatesAsync(AgentChecks.UpdateOffset(10), CancellationToken.None);

        // Assert
        Result<IReadOnlyList<IncomingUpdate>>.Success result = Assert.IsType<Result<IReadOnlyList<IncomingUpdate>>.Success>(actualResult);
        Assert.Equal(new IncomingUpdate(AgentChecks.UpdateId(17), AgentChecks.ChatId(42), TelegramChecks.TelegramText("Привет")), result.Value[0]);
        Assert.Equal(new IncomingUpdate(AgentChecks.UpdateId(18), AgentChecks.ChatId(42), null), result.Value[1]);
        Assert.Equal(new IncomingUpdate(AgentChecks.UpdateId(19), null, null), result.Value[2]);
        using var body = JsonDocument.Parse(transport.Body!);
        Assert.Equal(10, body.RootElement.GetProperty("offset").GetInt32());
    }

    [Fact]
    public async Task CallTelegram_GroupMessages_AreIgnoredBeforeConstructingMessageValues()
    {
        // Arrange
        var transport = new TelegramTransport
        {
            UpdatesJson = "[{\"update_id\":17,\"message\":{\"message_id\":1,\"date\":0,\"chat\":{\"id\":-42,\"type\":\"group\"},\"text\":\"\"}}]",
        };
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));

        // Act
        Result<IReadOnlyList<IncomingUpdate>> actualResult = await gateway.GetUpdatesAsync(AgentChecks.UpdateOffset(0), CancellationToken.None);

        // Assert
        Result<IReadOnlyList<IncomingUpdate>>.Success result = Assert.IsType<Result<IReadOnlyList<IncomingUpdate>>.Success>(actualResult);

        var update = Assert.Single(result.Value);
        Assert.Equal(AgentChecks.UpdateId(17), update.Id);
        Assert.Null(update.ChatId);
        Assert.Null(update.Text);
    }

    [Theory]
    [InlineData(-1, 42, "Привет", ErrorCode.InvalidUpdateId)]
    [InlineData(17, 0, "Привет", ErrorCode.InvalidChatId)]
    [InlineData(17, 42, "", ErrorCode.InvalidMessageText)]
    public async Task CallTelegram_InvalidIncomingValues_ReturnSafeFailure(int id, long chatId, string text, ErrorCode expectedCode)
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
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));

        // Act
        Result<IReadOnlyList<IncomingUpdate>> result = await gateway.GetUpdatesAsync(AgentChecks.UpdateOffset(0), CancellationToken.None);

        // Assert
        Result<IReadOnlyList<IncomingUpdate>>.Failure failure = Assert.IsType<Result<IReadOnlyList<IncomingUpdate>>.Failure>(result);
        Assert.Equal(expectedCode, failure.Error.Code);
        Assert.DoesNotContain(text == "" ? "secret" : text, failure.Message);
        Assert.False(failure.Error.IsRetryable);
    }

    [Fact]
    public async Task CallTelegram_OversizedIncomingText_ReturnsFailureAtAdapterBoundary()
    {
        // Arrange
        var text = new string('a', 4097);
        var transport = new TelegramTransport
        {
            UpdatesJson = "[{\"update_id\":17,\"message\":{\"message_id\":1,\"date\":0,\"chat\":{\"id\":42,\"type\":\"private\"},\"text\":"
                + JsonSerializer.Serialize(text) + "}}]",
        };
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));

        // Act
        Result<IReadOnlyList<IncomingUpdate>> actualResult = await gateway.GetUpdatesAsync(AgentChecks.UpdateOffset(0), CancellationToken.None);

        // Assert
        Result<IReadOnlyList<IncomingUpdate>>.Failure failure = Assert.IsType<Result<IReadOnlyList<IncomingUpdate>>.Failure>(actualResult);
        Assert.Equal(ErrorCode.InvalidMessageText, failure.Error.Code);
        Assert.DoesNotContain(text, failure.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ErrorCode.TelegramUnauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, ErrorCode.TelegramForbidden, false)]
    [InlineData(HttpStatusCode.Conflict, ErrorCode.TelegramPollingConflict, false)]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorCode.TelegramRateLimited, true)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorCode.TelegramUnavailable, true)]
    public async Task CallTelegram_ApiFailures_AreClassifiedAndDoNotLeakDescriptions(HttpStatusCode status, ErrorCode code, bool retryable)
    {
        // Arrange
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(new TelegramTransport { Status = status }));

        // Act
        Result<IReadOnlyList<IncomingUpdate>> actualResult = await gateway.GetUpdatesAsync(AgentChecks.UpdateOffset(0), CancellationToken.None);

        // Assert
        Result<IReadOnlyList<IncomingUpdate>>.Failure failure = Assert.IsType<Result<IReadOnlyList<IncomingUpdate>>.Failure>(actualResult);
        Assert.Equal(code, failure.Error.Code);
        Assert.Equal(retryable, failure.Error.IsRetryable);
        Assert.DoesNotContain("secret-error", failure.Message);
    }

    [Fact]
    public async Task CallTelegram_Send_PreservesTextAndDoesNotEnableParseMode()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));

        // Act
        Result<int> actualResult = await gateway.SendTextAsync(new TestValues().Chat(42), new TestValues().Text("<b>текст</b>"), CancellationToken.None);

        // Assert
        Assert.IsType<Result<int>.Success>(actualResult);
        using var json = JsonDocument.Parse(transport.Body!);
        Assert.Equal("<b>текст</b>", json.RootElement.GetProperty("text").GetString());
        Assert.Equal(42, json.RootElement.GetProperty("chat_id").GetInt64());
        Assert.False(json.RootElement.TryGetProperty("parse_mode", out _));
    }

    [Fact]
    public async Task SendTextAsync_HtmlFormat_EnablesHtmlParseMode()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));

        // Act
        Result<int> actualResult = await gateway.SendTextAsync(AgentChecks.ChatId(42),
            TelegramChecks.TelegramText("<pre><code>int x = 1;</code></pre>"), CancellationToken.None, TelegramTextFormat.Html);

        // Assert
        Assert.IsType<Result<int>.Success>(actualResult);
        using var body = JsonDocument.Parse(transport.Body!);
        Assert.Equal("Html", body.RootElement.GetProperty("parse_mode").GetString());
        Assert.Equal("<pre><code>int x = 1;</code></pre>", body.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task CallTelegram_NetworkException_ReturnsSafeRetryableFailure()
    {
        // Arrange
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(new TelegramTransport
        {
            Error = new HttpRequestException("secret-network-error"),
        }));

        // Act
        Result<int> actualResult = await gateway.SendTextAsync(
            new TestValues().Chat(42), new TestValues().Text("Привет"), CancellationToken.None);

        // Assert
        Result<int>.Failure failure = Assert.IsType<Result<int>.Failure>(actualResult);
        Assert.Equal(ErrorCode.TelegramUnavailable, failure.Error.Code);
        Assert.True(failure.Error.IsRetryable);
        Assert.DoesNotContain("secret-network-error", failure.Message);
    }

    [Fact]
    public async Task CallTelegram_Timeout_ReturnsRetryableErrorButCallerCancellationPropagates()
    {
        // Arrange
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(new TelegramTransport
        {
            Error = new OperationCanceledException("secret-error"),
        }));

        // Act
        Result<string> actualResult = await gateway.InitializeAsync(CancellationToken.None);

        // Assert
        Result<string>.Failure failure = Assert.IsType<Result<string>.Failure>(actualResult);
        Assert.Equal(ErrorCode.TelegramTimeout, failure.Error.Code);
        Assert.True(failure.Error.IsRetryable);
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.GetUpdatesAsync(AgentChecks.UpdateOffset(0), stopping.Token));
    }

    [Theory]
    [InlineData("photo")]
    [InlineData("delete")]
    [InlineData("callback")]
    public async Task CallTelegram_RejectedOperation_ReturnsSafeErrorAndPreservesCallerCancellation(string operation)
    {
        // Arrange
        using var gateway = new TelegramGateway(new TestValues().Settings(),
            new HttpClient(new TelegramTransport { Status = HttpStatusCode.Forbidden }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var error = await RejectedOperationAsync(gateway, operation, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.TelegramForbidden, error.Code);
        Assert.DoesNotContain("secret-error", error.Message);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RejectedOperationAsync(gateway, operation, cancellation.Token));
    }

    private async Task<AppError> RejectedOperationAsync(TelegramGateway gateway, string operation, CancellationToken token)
    {
        using var photo = new MemoryStream(new byte[] { 1, 2, 3 });
        object result = operation switch
        {
            "photo" => await gateway.SendPhotoAsync(42, photo, token),
            "delete" => await gateway.DeleteMessageAsync(42, 17, token),
            "callback" => await gateway.AnswerCallbackAsync("callback_101", token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        return result switch
        {
            Result<int>.Failure failure => failure.Error,
            Result.Failure failure => failure.Error,
            _ => throw new InvalidOperationException("Ожидался отказ Telegram."),
        };
    }

}
