using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Mcp;
using ItmoBot.Application.Messaging;
using ItmoBot.Application.Reminders;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Integration.Agent;

public sealed class AgentTelegramIntegrationTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_AgentCommands_DiscoversToolsAndSavesZoneWithoutCallingDialogue()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();

        // Act
        await fixture.Handler.HandleAsync(fixture.Message("/tools"), CancellationToken.None);
        var tools = fixture.Telegram.Sent[^1].Item2;
        await fixture.Handler.HandleAsync(fixture.Message("/timezone", owner: 84), CancellationToken.None);
        var missing = fixture.Telegram.Sent[^1].Item2;
        await fixture.Handler.HandleAsync(fixture.Message("/timezone Asia/Tbilisi", owner: 84), CancellationToken.None);
        var changed = fixture.Telegram.Sent[^1].Item2;

        // Assert
        Assert.Contains("get_weather", tools);
        Assert.Contains("add_reminder", tools);
        Assert.Contains("после вашего подтверждения", tools);
        Assert.Contains("не задан", missing);
        Assert.Contains("Asia/Tbilisi", changed);
        Assert.Contains("+04:00", changed);
        Assert.Equal("Asia/Tbilisi", fixture.Storage.Value(await fixture.Storage.Users.GetTimeZoneAsync(AgentChecks.OwnerId(84), CancellationToken.None))!.Value.Id);
        Assert.Equal(0, fixture.Dialogue.Calls);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleUpdate_Zone_ChangeCancelsOldButtonsAndInvalidZonePreservesStoredValue()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var action = await fixture.Storage.PrepareAsync();

        // Act
        await fixture.Handler.HandleAsync(fixture.Message("/timezone Unknown/City"), CancellationToken.None);
        var invalid = fixture.Telegram.Sent[^1].Item2;
        var zone = fixture.Storage.Value(await fixture.Storage.Users.GetTimeZoneAsync(AgentChecks.OwnerId(42), CancellationToken.None));
        await fixture.Handler.HandleAsync(fixture.Message("/timezone Asia/Tbilisi"), CancellationToken.None);
        Result<bool> result = await fixture.Handler.HandleAsync(fixture.Callback(action, true), CancellationToken.None);

        // Assert
        Assert.Contains("Неизвестный часовой пояс", invalid);
        Assert.Equal("Europe/Moscow", zone!.Value.Id);
        Assert.Equal(ErrorCode.ActionCancelled, Assert.IsType<Result<bool>.Failure>(result).Error.Code);
        Assert.Equal(0L, await fixture.ReminderCountAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleUpdate_Prompt_UsesStoredParametersAndConfirmCreatesOneReminderAndRemovesButtons()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var action = await fixture.Storage.PrepareAsync();
        fixture.Dialogue.Action = action;

        // Act
        await fixture.Handler.HandleAsync(fixture.Message("Напомни про расписание", 100), CancellationToken.None);
        var prompt = fixture.Telegram.Sent[^1].Item2;
        var keyboard = Assert.Single(fixture.Telegram.InlineKeyboards);
        Result<bool> result = await fixture.Handler.HandleAsync(fixture.Callback(action, true), CancellationToken.None);
        var success = fixture.Telegram.Sent[^1].Item2;
        Result<bool> repeated = await fixture.Handler.HandleAsync(fixture.Callback(action, true), CancellationToken.None);

        // Assert
        Assert.Contains(action.Payload.Text, prompt);
        Assert.Contains("+03:00", prompt);
        Assert.DoesNotContain("Непроверенный текст модели", prompt);
        Assert.Equal(2, keyboard.Rows[0].Count);
        Assert.Equal(action.Id, new ReminderCallbackCodec().Decode(keyboard.Rows[0][0].Data)!.ActionId);
        Assert.IsType<Result<bool>.Success>(result);
        Assert.IsType<Result<bool>.Success>(repeated);
        Assert.Contains("Напоминание создано", success);
        Assert.Contains("Время:", success);
        Assert.Contains("ID:", success);
        Assert.Contains("уже было создано", fixture.Telegram.Sent[^1].Item2);
        Assert.Single(fixture.Telegram.InlineKeyboards);
        Assert.Equal(2, fixture.Telegram.AnsweredCallbacks.Count);
        Assert.Equal(2, fixture.Telegram.ClearedKeyboards.Count);
        Assert.Equal(1L, await fixture.ReminderCountAsync());
        Assert.Equal(1, fixture.Dialogue.Calls);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleUpdate_ConcurrentConfirmations_CreateOneReminder()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var action = await fixture.Storage.PrepareAsync();
        var owner = action.Owner;
        var button = new ReminderButtonAction(action.Id, true);

        // Act
        var results = await Task.WhenAll(
            fixture.Service.ExecuteAsync(new AgentTurnContext(owner.OwnerId, owner.ChatId, AgentChecks.UpdateId(101)), button, CancellationToken.None),
            fixture.Service.ExecuteAsync(new AgentTurnContext(owner.OwnerId, owner.ChatId, AgentChecks.UpdateId(102)), button, CancellationToken.None));

        // Assert
        Assert.All(results, result => Assert.IsType<ReminderInteractionResult.Success>(result));
        Assert.Single(results, result => ((ReminderInteractionResult.Success)result).Reply.StartsWith("Напоминание создано", StringComparison.Ordinal));
        Assert.Single(results, result => ((ReminderInteractionResult.Success)result).Reply.Contains("уже было создано", StringComparison.Ordinal));
        Assert.Equal(1L, await fixture.ReminderCountAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleUpdate_ForeignAndMissingSender_CannotConfirmOrRemoveOwnersButtons()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var action = await fixture.Storage.PrepareAsync();

        // Act
        Result<bool> foreign = await fixture.Handler.HandleAsync(fixture.Callback(action, true, owner: 84), CancellationToken.None);
        Result<bool> missing = await fixture.Handler.HandleAsync(fixture.Callback(action, true, 102) with
        {
            OwnerId = null
        }, CancellationToken.None);
        Result<bool> mismatch = await fixture.Handler.HandleAsync(fixture.Callback(action, true, 103, 84) with
        {
            ChatId = AgentChecks.ChatId(42)
        }, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.Unauthorized, Assert.IsType<Result<bool>.Failure>(foreign).Error.Code);
        Assert.Equal(ErrorCode.Unauthorized, Assert.IsType<Result<bool>.Failure>(missing).Error.Code);
        Assert.Equal(ErrorCode.Unauthorized, Assert.IsType<Result<bool>.Failure>(mismatch).Error.Code);
        Assert.Empty(fixture.Telegram.ClearedKeyboards);
        Assert.Equal(3, fixture.Telegram.AnsweredCallbacks.Count);
        Assert.Equal(0L, await fixture.ReminderCountAsync());
        Assert.Equal(PreparedActionStatus.Pending, fixture.Storage.Value(await fixture.Storage.Actions.GetActionAsync(action.Owner, action.Id, CancellationToken.None)).Status);
        Assert.DoesNotContain(action.Payload.Text, fixture.Telegram.Sent[^1].Item2);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleUpdate_CancelAndExpiredConfirmation_NeverCreateReminder()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var cancelled = await fixture.Storage.PrepareAsync();

        // Act
        await fixture.Handler.HandleAsync(fixture.Callback(cancelled, false), CancellationToken.None);
        var cancellation = fixture.Telegram.Sent[^1].Item2;
        Result<bool> cancelReplay = await fixture.Handler.HandleAsync(fixture.Callback(cancelled, true, 102), CancellationToken.None);
        var expired = await fixture.Storage.PrepareAsync(103);
        fixture.Storage.Clock.Advance(TimeSpan.FromMinutes(5));
        Result<bool> result = await fixture.Handler.HandleAsync(fixture.Callback(expired, true, 104), CancellationToken.None);

        // Assert
        Assert.Contains("отменено", cancellation);
        Assert.Equal(ErrorCode.ActionCancelled, Assert.IsType<Result<bool>.Failure>(cancelReplay).Error.Code);
        Assert.Equal(ErrorCode.ActionExpired, Assert.IsType<Result<bool>.Failure>(result).Error.Code);
        Assert.Contains("истёк", fixture.Telegram.Sent[^1].Item2);
        Assert.Equal(0L, await fixture.ReminderCountAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleUpdate_McpFailure_KeepsConfirmedActionRetryableAndDoesNotClaimSuccess()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var action = await fixture.Storage.PrepareAsync();
        var unavailable = new StubAgentRuntime(new Result<McpCatalog>.Failure(new AppError(ErrorCode.McpUnavailable, "Недоступно")));
        var service = new ReminderConfirmationService(fixture.Storage.Actions, unavailable, fixture.Storage.Audit, fixture.Storage.Clock, NullLogger.Instance);
        var button = new ReminderButtonAction(action.Id, true);
        var owner = new AgentTurnContext(action.Owner.OwnerId, action.Owner.ChatId, AgentChecks.UpdateId(101));

        // Act
        var failed = await service.ExecuteAsync(owner, button, CancellationToken.None);
        var state = fixture.Storage.Value(await fixture.Storage.Actions.GetActionAsync(action.Owner, action.Id, CancellationToken.None)).Status;
        Result<bool> repeated = await fixture.Handler.HandleAsync(fixture.Callback(action, true, 102), CancellationToken.None);

        // Assert
        var failure = Assert.IsType<ReminderInteractionResult.Failure>(failed);
        Assert.Equal(ErrorCode.McpUnavailable, failure.Error.Code);
        Assert.False(failure.RemoveButtons);
        Assert.Equal(PreparedActionStatus.Confirmed, state);
        Assert.IsType<Result<bool>.Success>(repeated);
        Assert.Equal(1L, await fixture.ReminderCountAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_WhyCommand_ShowsOnlyOwnSafeAudit()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var action = await fixture.Storage.PrepareAsync();
        await fixture.Handler.HandleAsync(fixture.Callback(action, true), CancellationToken.None);

        // Act
        await fixture.Handler.HandleAsync(fixture.Message("/why"), CancellationToken.None);
        var own = fixture.Telegram.Sent[^1].Item2;
        await fixture.Handler.HandleAsync(fixture.Message("/why", owner: 84), CancellationToken.None);
        var other = fixture.Telegram.Sent[^1].Item2;

        // Assert
        Assert.Contains("add_reminder", own);
        Assert.Contains("успешно", own);
        Assert.Contains("Europe/Moscow", own);
        Assert.Contains("Длительность", own);
        Assert.DoesNotContain(action.Payload.Text, own);
        Assert.DoesNotContain(action.Id.ToString(), own);
        Assert.DoesNotContain("signature", own);
        Assert.DoesNotContain("owner_id", own);
        Assert.Contains("нет записей", other);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleUpdate_NewBotSession_CanConfirmPersistedActionAndRepeatResult()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var action = await fixture.Storage.PrepareAsync();
        await fixture.Runtime.DisposeAsync();
        await using var restarted = fixture.CreateRuntime();
        Assert.IsType<Result<McpCatalog>.Success>(await restarted.InitializeAsync(CancellationToken.None));
        var service = new ReminderConfirmationService(fixture.Storage.Actions, restarted, fixture.Storage.Audit, fixture.Storage.Clock, NullLogger.Instance);
        var context = new AgentTurnContext(action.Owner.OwnerId, action.Owner.ChatId, AgentChecks.UpdateId(101));

        // Act
        var first = await service.ExecuteAsync(context, new ReminderButtonAction(action.Id, true), CancellationToken.None);
        var repeated = await service.ExecuteAsync(context, new ReminderButtonAction(action.Id, true), CancellationToken.None);

        // Assert
        Assert.IsType<ReminderInteractionResult.Success>(first);
        Assert.Contains("уже было создано", Assert.IsType<ReminderInteractionResult.Success>(repeated).Reply);
        Assert.Equal(1L, await fixture.ReminderCountAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleUpdate_FailureToRemoveKeyboard_DoesNotHidePersistedSuccess()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var action = await fixture.Storage.PrepareAsync();
        fixture.Telegram.EditError = new AppError(ErrorCode.TelegramUnavailable, "Тестовый отказ изменения кнопок");

        // Act
        Result<bool> result = await fixture.Handler.HandleAsync(fixture.Callback(action, true), CancellationToken.None);

        // Assert
        Assert.IsType<Result<bool>.Success>(result);
        Assert.Contains("Напоминание создано", fixture.Telegram.Sent[^1].Item2);
        Assert.Equal(1L, await fixture.ReminderCountAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleUpdate_UnknownCallback_IsAcknowledgedWithoutMutation()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var action = await fixture.Storage.PrepareAsync();
        var update = fixture.Callback(action, true) with
        {
            Callback = new TelegramCallback(TelegramChecks.CallbackQueryId("callback_unknown"), TelegramChecks.CallbackData("unknown-button"), TelegramChecks.MessageId(1)),
        };

        // Act
        Result<bool> result = await fixture.Handler.HandleAsync(update, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.InvalidCallback, Assert.IsType<Result<bool>.Failure>(result).Error.Code);
        Assert.Single(fixture.Telegram.AnsweredCallbacks);
        Assert.Empty(fixture.Telegram.ClearedKeyboards);
        Assert.Equal(0L, await fixture.ReminderCountAsync());
        Assert.Equal(0, fixture.Dialogue.Calls);
    }


    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleUpdate_CallbackHandler_DoesNotAcquireDialogueGenerationLock()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var action = await fixture.Storage.PrepareAsync();
        await using var held = await fixture.Conversations.AcquireAsync(action.Owner.ChatId, CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Act
        Result<bool> result = await fixture.Handler.HandleAsync(fixture.Callback(action, true), timeout.Token);

        // Assert
        Assert.IsType<Result<bool>.Success>(result);
        Assert.Single(fixture.Telegram.AnsweredCallbacks);
        Assert.Equal(1L, await fixture.ReminderCountAsync());
    }


    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task HandleUpdate_ReplayedOriginalUpdateAndConcurrentConfirmation_CreateOneActionAndReminder()
    {
        // Arrange
        await using var fixture = new AgentTelegramFixture();
        await fixture.InitializeAsync();
        var first = await fixture.Storage.PrepareAsync(100);
        var replayed = await fixture.Storage.PrepareAsync(100);
        var context = new AgentTurnContext(first.Owner.OwnerId, first.Owner.ChatId, AgentChecks.UpdateId(101));

        // Act
        var results = await Task.WhenAll(
            fixture.Service.ExecuteAsync(context, new ReminderButtonAction(first.Id, true), CancellationToken.None),
            fixture.Service.ExecuteAsync(context, new ReminderButtonAction(replayed.Id, true), CancellationToken.None));

        // Assert
        Assert.Equal(first.Id, replayed.Id);
        Assert.All(results, result => Assert.IsType<ReminderInteractionResult.Success>(result));
        await using var counts = fixture.Storage.Storage.Source.CreateCommand("""
            SELECT (SELECT count(*) FROM prepared_actions),
                   (SELECT count(*) FROM action_confirmations),
                   (SELECT count(*) FROM reminders)
            """);
        await using var reader = await counts.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
    }

}
