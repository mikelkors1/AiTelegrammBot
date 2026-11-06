using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using Xunit;

namespace ItmoBot.Tests.Integration.Storage;

public sealed class AgentRepositoryIntegrationTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task PrepareActionAsync_ConcurrentPreparation_ReturnsOneActionForOriginalUpdate()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var payload = fixture.Payload();

        // Act
        var results = await Task.WhenAll(
            fixture.Actions.PrepareReminderAsync(fixture.Owner(), AgentChecks.UpdateId(100), payload, CancellationToken.None),
            fixture.Actions.PrepareReminderAsync(fixture.Owner(), AgentChecks.UpdateId(100), payload, CancellationToken.None));

        // Assert
        var actions = results.Select(fixture.Value).ToArray();
        Assert.Equal(actions[0].Id, actions[1].Id);
        Assert.Equal(actions[0].ArgumentsHash, actions[1].ArgumentsHash);
        Assert.Equal(PreparedActionStatus.Pending, actions[0].Status);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task SetTimeZoneAsync_Zone_ChangeCancelsOnlyOwnersPendingActions()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var first = await fixture.PrepareAsync();
        await fixture.Users.SetTimeZoneAsync(AgentChecks.OwnerId(84), new UserTimeZone("Europe/Moscow"), CancellationToken.None);
        var second = fixture.Value(await fixture.Actions.PrepareReminderAsync(fixture.Owner(84), AgentChecks.UpdateId(100), fixture.Payload(), CancellationToken.None));

        // Act
        await fixture.Users.SetTimeZoneAsync(AgentChecks.OwnerId(42), new UserTimeZone("Asia/Tbilisi"), CancellationToken.None);

        // Assert
        Assert.Equal("Asia/Tbilisi", fixture.Value(await fixture.Users.GetTimeZoneAsync(AgentChecks.OwnerId(42), CancellationToken.None))!.Value.Id);
        Assert.Equal(PreparedActionStatus.Cancelled, fixture.Value(await fixture.Actions.GetActionAsync(fixture.Owner(), first.Id, CancellationToken.None)).Status);
        Assert.Equal(PreparedActionStatus.Pending, fixture.Value(await fixture.Actions.GetActionAsync(fixture.Owner(84), second.Id, CancellationToken.None)).Status);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task PrepareActionAsync_MissingZone_RejectsPreparationAndSameZonePreservesPendingAction()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var action = await fixture.PrepareAsync();

        // Act
        var missing = await fixture.Actions.PrepareReminderAsync(fixture.Owner(84), AgentChecks.UpdateId(100), fixture.Payload(), CancellationToken.None);
        await fixture.Users.SetTimeZoneAsync(AgentChecks.OwnerId(42), new UserTimeZone("Europe/Moscow"), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.TimeZoneNotSet, Assert.IsType<AgentStorageResult<PreparedAction>.Failure>(missing).Error.Code);
        Assert.Null(fixture.Value(await fixture.Users.GetTimeZoneAsync(AgentChecks.OwnerId(84), CancellationToken.None)));
        Assert.Equal(PreparedActionStatus.Pending, fixture.Value(await fixture.Actions.GetActionAsync(fixture.Owner(), action.Id, CancellationToken.None)).Status);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task PrepareActionAsync_ChangedArguments_CannotReuseOriginalUpdateAndNewUpdateReplacesPendingAction()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var previous = await fixture.PrepareAsync();

        // Act
        var mismatch = await fixture.Actions.PrepareReminderAsync(fixture.Owner(), AgentChecks.UpdateId(100), fixture.Payload("Другой текст"), CancellationToken.None);
        var next = fixture.Value(await fixture.Actions.PrepareReminderAsync(fixture.Owner(), AgentChecks.UpdateId(101), fixture.Payload("Другой текст"), CancellationToken.None));

        // Assert
        Assert.Equal(ErrorCode.ActionMismatch, Assert.IsType<AgentStorageResult<PreparedAction>.Failure>(mismatch).Error.Code);
        Assert.NotEqual(previous.Id, next.Id);
        Assert.Equal(PreparedActionStatus.Cancelled, fixture.Value(await fixture.Actions.GetActionAsync(fixture.Owner(), previous.Id, CancellationToken.None)).Status);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task AccessAction_ForeignOwner_CannotReadConfirmCancelOrExecuteAction()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var action = await fixture.PrepareAsync();
        var other = fixture.Owner(84);

        // Act
        var read = await fixture.Actions.GetActionAsync(other, action.Id, CancellationToken.None);
        var confirm = await fixture.Actions.ConfirmActionAsync(other, action.Id, AgentChecks.UpdateId(101), CancellationToken.None);
        var cancel = await fixture.Actions.CancelActionAsync(other, action.Id, CancellationToken.None);
        var execute = await fixture.Reminders.ExecuteConfirmedActionAsync(other, action.Id, action.ArgumentsHash, CancellationToken.None);

        // Assert
        Assert.IsType<AgentStorageResult<PreparedAction>.NotFound>(read);
        Assert.IsType<AgentStorageResult<PreparedAction>.NotFound>(confirm);
        Assert.IsType<AgentStorageResult<PreparedAction>.NotFound>(cancel);
        Assert.IsType<AgentStorageResult<ReminderCreation>.NotFound>(execute);
        Assert.Equal(PreparedActionStatus.Pending, fixture.Value(await fixture.Actions.GetActionAsync(fixture.Owner(), action.Id, CancellationToken.None)).Status);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ConfirmActionAsync_ConfirmationAtExpiry_PersistsExpiredStateAndCannotCreateReminder()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var action = await fixture.PrepareAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var confirm = await fixture.Actions.ConfirmActionAsync(fixture.Owner(), action.Id, AgentChecks.UpdateId(101), CancellationToken.None);
        var execute = await fixture.Reminders.ExecuteConfirmedActionAsync(fixture.Owner(), action.Id, action.ArgumentsHash, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ActionExpired, Assert.IsType<AgentStorageResult<PreparedAction>.Failure>(confirm).Error.Code);
        Assert.Equal(ErrorCode.ActionExpired, Assert.IsType<AgentStorageResult<ReminderCreation>.Failure>(execute).Error.Code);
        Assert.Equal(PreparedActionStatus.Expired, fixture.Value(await fixture.Actions.GetActionAsync(fixture.Owner(), action.Id, CancellationToken.None)).Status);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ConfirmAndExecute_ConcurrentConfirmationAndExecution_ReturnOneReminderAndRepeatAfterDueTimeReturnsIt()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var action = await fixture.PrepareAsync();
        var owner = fixture.Owner();
        var confirmations = await Task.WhenAll(
            fixture.Actions.ConfirmActionAsync(owner, action.Id, AgentChecks.UpdateId(101), CancellationToken.None),
            fixture.Actions.ConfirmActionAsync(owner, action.Id, AgentChecks.UpdateId(101), CancellationToken.None));
        Assert.All(confirmations, result => Assert.Equal(PreparedActionStatus.Confirmed, fixture.Value(result).Status));

        // Act
        var results = await Task.WhenAll(
            fixture.Reminders.ExecuteConfirmedActionAsync(owner, action.Id, action.ArgumentsHash, CancellationToken.None),
            fixture.Reminders.ExecuteConfirmedActionAsync(owner, action.Id, action.ArgumentsHash, CancellationToken.None));
        var stored = results.Select(fixture.Value).ToArray();
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        await using var newSource = fixture.Storage.CreateNewSource();
        var restarted = fixture.CreateReminderRepository(newSource);
        var repeated = fixture.Value(await restarted.ExecuteConfirmedActionAsync(owner, action.Id, action.ArgumentsHash, CancellationToken.None));

        // Assert
        Assert.Single(stored, value => value.Created);
        Assert.Single(stored, value => !value.Created);
        Assert.Equal(stored[0].Reminder.Id, stored[1].Reminder.Id);
        Assert.False(repeated.Created);
        Assert.Equal(stored[0].Reminder.Id, repeated.Reminder.Id);
        Assert.Equal(PreparedActionStatus.Executed, fixture.Value(await fixture.Actions.GetActionAsync(owner, action.Id, CancellationToken.None)).Status);
        Assert.IsType<AgentStorageResult<StoredReminder>.NotFound>(await fixture.Reminders.GetReminderAsync(fixture.Owner(84), repeated.Reminder.Id, CancellationToken.None));
        Assert.Equal(action.Payload, fixture.Value(await fixture.Reminders.GetReminderAsync(owner, repeated.Reminder.Id, CancellationToken.None)).Payload);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ExecuteReminderAsync_UnconfirmedCancelledAndChangedArguments_AreRejected()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var action = await fixture.PrepareAsync();

        // Act
        var unconfirmed = await fixture.Reminders.ExecuteConfirmedActionAsync(fixture.Owner(), action.Id, action.ArgumentsHash, CancellationToken.None);
        var mismatch = await fixture.Reminders.ExecuteConfirmedActionAsync(fixture.Owner(), action.Id, AgentChecks.ArgumentsHash(new string('A', 64)), CancellationToken.None);
        await fixture.Actions.CancelActionAsync(fixture.Owner(), action.Id, CancellationToken.None);
        var cancelled = await fixture.Reminders.ExecuteConfirmedActionAsync(fixture.Owner(), action.Id, action.ArgumentsHash, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ActionNotConfirmed, Assert.IsType<AgentStorageResult<ReminderCreation>.Failure>(unconfirmed).Error.Code);
        Assert.Equal(ErrorCode.ActionMismatch, Assert.IsType<AgentStorageResult<ReminderCreation>.Failure>(mismatch).Error.Code);
        Assert.Equal(ErrorCode.ActionCancelled, Assert.IsType<AgentStorageResult<ReminderCreation>.Failure>(cancelled).Error.Code);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ExecuteReminderAsync_FailedWrite_RollsBackReminderAndKeepsActionConfirmedForRetry()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var action = await fixture.PrepareAsync();
        await fixture.Actions.ConfirmActionAsync(fixture.Owner(), action.Id, AgentChecks.UpdateId(101), CancellationToken.None);
        await using (var constraint = fixture.Storage.Source.CreateCommand("ALTER TABLE prepared_actions ADD CONSTRAINT test_reject_execution CHECK (status <> 'executed')"))
        {
            await constraint.ExecuteNonQueryAsync();
        }

        // Act
        var failed = await fixture.Reminders.ExecuteConfirmedActionAsync(fixture.Owner(), action.Id, action.ArgumentsHash, CancellationToken.None);
        var pending = fixture.Value(await fixture.Actions.GetActionAsync(fixture.Owner(), action.Id, CancellationToken.None));
        await using var count = fixture.Storage.Source.CreateCommand("SELECT count(*) FROM reminders");
        var remaining = await count.ExecuteScalarAsync();
        await using (var constraint = fixture.Storage.Source.CreateCommand("ALTER TABLE prepared_actions DROP CONSTRAINT test_reject_execution"))
        {
            await constraint.ExecuteNonQueryAsync();
        }

        var repeated = fixture.Value(await fixture.Reminders.ExecuteConfirmedActionAsync(fixture.Owner(), action.Id, action.ArgumentsHash, CancellationToken.None));

        // Assert
        var error = Assert.IsType<AgentStorageResult<ReminderCreation>.Failure>(failed).Error;
        Assert.Equal(ErrorCode.DatabaseOperationFailed, error.Code);
        Assert.DoesNotContain("test_reject_execution", error.Message);
        Assert.Equal(PreparedActionStatus.Confirmed, pending.Status);
        Assert.Equal(0L, remaining);
        Assert.True(repeated.Created);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task GetLatestEventAsync_Audit_ReturnsOnlyLatestEventOfRequestedOwnerAndChat()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var entry = new AgentAuditEvent(AgentChecks.AgentRequestId(Guid.NewGuid()), AgentChecks.OwnerId(42), AgentChecks.ChatId(42), AgentChecks.UpdateId(100),
            fixture.Clock.GetUtcNow(), AuditAction.ToolCall, AuditStatus.Succeeded, AgentChecks.AuditReason("Подтверждено создание напоминания"),
            new AuditArgumentsSummary(20, fixture.Payload().Schedule.Moment, new UserTimeZone("Europe/Moscow")), AgentChecks.McpToolName("add_reminder"), TimeSpan.FromMilliseconds(25));
        await fixture.Audit.AppendEventAsync(entry, CancellationToken.None);
        var next = entry with
        {
            RequestId = AgentChecks.AgentRequestId(Guid.NewGuid()),
            Status = AuditStatus.LimitExceeded
        };
        await fixture.Audit.AppendEventAsync(next, CancellationToken.None);
        await fixture.Audit.AppendEventAsync(entry with
        {
            Owner = AgentChecks.OwnerId(84),
            ChatId = AgentChecks.ChatId(84)
        }, CancellationToken.None);

        // Act
        var latest = fixture.Value(await fixture.Audit.GetLatestEventAsync(AgentChecks.OwnerId(42), AgentChecks.ChatId(42), CancellationToken.None));
        var otherChat = await fixture.Audit.GetLatestEventAsync(AgentChecks.OwnerId(42), AgentChecks.ChatId(84), CancellationToken.None);

        // Assert
        Assert.Equal(next, latest);
        Assert.IsType<AgentStorageResult<AgentAuditEvent>.NotFound>(otherChat);
        Assert.Equal("add_reminder", latest.Tool!);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task PrepareActionAsync_CallerCancellation_PropagatesWithoutPreparingAction()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var running = fixture.Actions.PrepareReminderAsync(fixture.Owner(), AgentChecks.UpdateId(100), fixture.Payload(), cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await using var count = fixture.Storage.Source.CreateCommand("SELECT count(*) FROM prepared_actions");
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ConfirmActionAsync_ConfirmationUpdate_CannotConfirmAnotherAction()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var first = await fixture.PrepareAsync();
        await fixture.Actions.ConfirmActionAsync(fixture.Owner(), first.Id, AgentChecks.UpdateId(101), CancellationToken.None);
        var second = await fixture.PrepareAsync(102);

        // Act
        var repeated = await fixture.Actions.ConfirmActionAsync(fixture.Owner(), second.Id, AgentChecks.UpdateId(101), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ActionMismatch, Assert.IsType<AgentStorageResult<PreparedAction>.Failure>(repeated).Error.Code);
        Assert.Equal(PreparedActionStatus.Pending, fixture.Value(await fixture.Actions.GetActionAsync(fixture.Owner(), second.Id, CancellationToken.None)).Status);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task CancelAndExecute_ConfirmedAction_CannotBeCancelledButCanExecuteAfterConfirmationDeadline()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var action = await fixture.PrepareAsync();
        await fixture.Actions.ConfirmActionAsync(fixture.Owner(), action.Id, AgentChecks.UpdateId(101), CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));

        // Act
        var cancel = await fixture.Actions.CancelActionAsync(fixture.Owner(), action.Id, CancellationToken.None);
        var result = await fixture.Reminders.ExecuteConfirmedActionAsync(fixture.Owner(), action.Id, action.ArgumentsHash, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ActionAlreadyConfirmed, Assert.IsType<AgentStorageResult<PreparedAction>.Failure>(cancel).Error.Code);
        Assert.True(fixture.Value(result).Created);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ExecuteReminderAsync_Execution_RejectsPastTimeAndChangedStoredPayload()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        var action = await fixture.PrepareAsync();
        await fixture.Actions.ConfirmActionAsync(fixture.Owner(), action.Id, AgentChecks.UpdateId(101), CancellationToken.None);
        await using var change = fixture.Storage.Source.CreateCommand("UPDATE prepared_actions SET reminder_text = 'Изменённый текст' WHERE action_id = $1");
        change.Parameters.AddWithValue(action.Id);
        await change.ExecuteNonQueryAsync();

        // Act
        var mismatch = await fixture.Reminders.ExecuteConfirmedActionAsync(fixture.Owner(), action.Id, action.ArgumentsHash, CancellationToken.None);
        change.CommandText = "UPDATE prepared_actions SET reminder_text = 'Проверить расписание' WHERE action_id = $1";
        await change.ExecuteNonQueryAsync();
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        var past = await fixture.Reminders.ExecuteConfirmedActionAsync(fixture.Owner(), action.Id, action.ArgumentsHash, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.ActionMismatch, Assert.IsType<AgentStorageResult<ReminderCreation>.Failure>(mismatch).Error.Code);
        Assert.Equal(ErrorCode.ReminderTimeInPast, Assert.IsType<AgentStorageResult<ReminderCreation>.Failure>(past).Error.Code);
        await using var count = fixture.Storage.Source.CreateCommand("SELECT count(*) FROM reminders");
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task GetTimeZoneAsync_InvalidTimezoneInStorage_ReturnsSafeFailure()
    {
        // Arrange
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        await using var change = fixture.Storage.Source.CreateCommand("UPDATE agent_users SET timezone = 'Unknown/PrivateCity' WHERE owner_id = 42");
        await change.ExecuteNonQueryAsync();

        // Act
        var result = await fixture.Users.GetTimeZoneAsync(AgentChecks.OwnerId(42), CancellationToken.None);

        // Assert
        var error = Assert.IsType<AgentStorageResult<UserTimeZone?>.Failure>(result).Error;
        Assert.Equal(ErrorCode.InvalidAgentData, error.Code);
        Assert.DoesNotContain("PrivateCity", error.Message);
    }

}
