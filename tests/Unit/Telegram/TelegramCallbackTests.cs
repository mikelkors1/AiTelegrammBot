using System.Text.Json;
using ItmoBot.Application.Messaging;
using ItmoBot.Application.Reminders;
using ItmoBot.Infrastructure.Telegram;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Telegram;

public sealed class TelegramCallbackTests
{
    [Fact]
    public async Task HandleCallback_Callback_KeepsSenderSeparateFromChatAndPollingIncludesCallbacks()
    {
        // Arrange
        var transport = new TelegramTransport
        {
            UpdatesJson = """
                [{"update_id":101,"callback_query":{"id":"callback_101","chat_instance":"test","from":{"id":84,"is_bot":false,"first_name":"Test"},
                "message":{"message_id":7,"date":0,"chat":{"id":42,"type":"private"}},"data":"r:y:33333333333333333333333333333333"}}]
                """,
        };
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));

        // Act
        Result<IReadOnlyList<IncomingUpdate>> result = await gateway.GetUpdatesAsync(AgentChecks.UpdateOffset(0), CancellationToken.None);

        // Assert
        var update = Assert.Single(Assert.IsType<Result<IReadOnlyList<IncomingUpdate>>.Success>(result).Value);
        Assert.Equal(84, update.OwnerId!);
        Assert.Equal(42, update.ChatId!);
        Assert.Null(update.Text);
        Assert.Equal("callback_101", update.Callback!.Id);
        Assert.Equal(7, update.Callback.MessageId!);
        using var body = JsonDocument.Parse(transport.Body!);
        Assert.Contains(body.RootElement.GetProperty("allowed_updates").EnumerateArray(), value => value.GetString() == "callback_query");
    }

    [Fact]
    public async Task HandleCallback_InlineKeyboard_ContainsOpaqueActionAndAckAndClearUseDedicatedMethods()
    {
        // Arrange
        var transport = new TelegramTransport();
        using var gateway = new TelegramGateway(new TestValues().Settings(), new HttpClient(transport));
        var action = new TrustedActionTestValues().Action(TimeProvider.System);
        var codec = new ReminderCallbackCodec();
        string data = codec.Encode(new ReminderButtonAction(action.Id, true));
        var keyboard = new TelegramInlineKeyboard(new IReadOnlyList<TelegramInlineButton>[] { new TelegramInlineButton[] { new(TelegramChecks.TelegramText("Подтвердить"), data) } });

        // Act
        Result<int> sent = await gateway.SendInlineTextAsync(action.Owner.ChatId, TelegramChecks.TelegramText("Подтвердите"), keyboard, CancellationToken.None);
        using var send = JsonDocument.Parse(transport.Body!);
        var acknowledged = await gateway.AnswerCallbackAsync(TelegramChecks.CallbackQueryId("callback_101"), CancellationToken.None);
        var removed = await gateway.RemoveInlineKeyboardAsync(action.Owner.ChatId, TelegramChecks.MessageId(7), CancellationToken.None);

        // Assert
        Assert.IsType<Result<int>.Success>(sent);
        Assert.IsType<Result.Success>(acknowledged);
        Assert.IsType<Result.Success>(removed);
        Assert.Equal(data, send.RootElement.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0].GetProperty("callback_data").GetString());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(data) <= 64);
        Assert.DoesNotContain("owner", data);
        Assert.Equal(new[] { "sendMessage", "answerCallbackQuery", "editMessageReplyMarkup" }, transport.Methods);
    }


}
