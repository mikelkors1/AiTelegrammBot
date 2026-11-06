using System.Text.Json;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Mcp;
using ItmoBot.Infrastructure.Mcp;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Integration.Reminders;

public sealed class ReminderMcpIntegrationTests
{
    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ExecuteReminder_IndependentClient_DiscoversSchemaAndCreatesOneConfirmedReminderConcurrently()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var fixture = new ReminderMcpFixture();
        await fixture.InitializeAsync(timeout.Token);
        var action = await fixture.Storage.PrepareAsync();
        await fixture.Storage.Actions.ConfirmActionAsync(action.Owner, action.Id, AgentChecks.UpdateId(101), timeout.Token);

        // Act
        var tools = await fixture.Client.ListToolsAsync(cancellationToken: timeout.Token);
        var results = await Task.WhenAll(fixture.Client.CallToolAsync(fixture.Request(action), timeout.Token).AsTask(),
            fixture.Client.CallToolAsync(fixture.Request(action), timeout.Token).AsTask());

        // Assert
        Assert.Equal(3, tools.Count);
        var tool = Assert.Single(tools, item => item.ProtocolTool.Name == "add_reminder").ProtocolTool;
        Assert.Equal(new[] { "remind_at", "text" }, tool.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order());
        Assert.False(tool.Annotations!.ReadOnlyHint);
        Assert.True(tool.Annotations.IdempotentHint);
        Assert.NotNull(tool.OutputSchema);
        Assert.All(results, result => Assert.False(result.IsError));
        var values = results.Select(result => result.StructuredContent!.Value).ToArray();
        Assert.Single(values, value => value.GetProperty("created").GetBoolean());
        Assert.Single(values, value => !value.GetProperty("created").GetBoolean());
        Assert.Equal(values[0].GetProperty("reminder_id").GetGuid(), values[1].GetProperty("reminder_id").GetGuid());
        Assert.Equal("Europe/Moscow", values[0].GetProperty("timezone").GetString());
        Assert.Equal(4, values[0].EnumerateObject().Count());
        await using var count = fixture.Storage.Storage.Source.CreateCommand("SELECT count(*) FROM reminders");
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ExecuteReminder_MissingForgedExpiredAndForeignContext_CannotCreateReminder()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var fixture = new ReminderMcpFixture();
        await fixture.InitializeAsync(timeout.Token);
        var action = await fixture.Storage.PrepareAsync();
        await fixture.Storage.Actions.ConfirmActionAsync(action.Owner, action.Id, AgentChecks.UpdateId(101), timeout.Token);
        var missing = fixture.Request(action);
        missing.Meta = null;
        var forged = fixture.Request(action);
        forged.Meta!["itmobot/action"]!["owner_id"] = 84;
        forged.Meta["itmobot/action"]!["chat_id"] = 84;
        var expired = fixture.Request(action);
        fixture.Storage.Clock.Advance(TimeSpan.FromSeconds(60));
        var foreignAction = action with
        {
            Owner = fixture.Storage.Owner(84)
        };

        // Act
        var results = new[]
        {
            await fixture.Client.CallToolAsync(missing, timeout.Token),
            await fixture.Client.CallToolAsync(forged, timeout.Token),
            await fixture.Client.CallToolAsync(expired, timeout.Token),
            await fixture.Client.CallToolAsync(fixture.Request(foreignAction), timeout.Token),
        };

        // Assert
        Assert.All(results, result =>
        {
            Assert.True(result.IsError);
            Assert.Equal("unauthorized", result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        });
        await using var count = fixture.Storage.Storage.Source.CreateCommand("SELECT count(*) FROM reminders");
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ExecuteReminder_ValidSignature_CannotReplaceConfirmationOrChangeItsArguments()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var fixture = new ReminderMcpFixture();
        await fixture.InitializeAsync(timeout.Token);
        var action = await fixture.Storage.PrepareAsync();

        // Act
        var pending = await fixture.Client.CallToolAsync(fixture.Request(action), timeout.Token);
        await fixture.Storage.Actions.ConfirmActionAsync(action.Owner, action.Id, AgentChecks.UpdateId(101), timeout.Token);
        var changed = fixture.Request(action);
        changed.Arguments = new Dictionary<string, JsonElement>(changed.Arguments!)
        {
            ["text"] = JsonSerializer.SerializeToElement("Изменённый текст"),
        };
        var mismatch = await fixture.Client.CallToolAsync(changed, timeout.Token);

        // Assert
        Assert.Equal("action_not_confirmed", pending.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("action_mismatch", mismatch.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        await using var count = fixture.Storage.Storage.Source.CreateCommand("SELECT count(*) FROM reminders");
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ExecuteReminder_CancelledAndExpiredActions_CannotBeExecutedWithFreshSignature()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var fixture = new ReminderMcpFixture();
        await fixture.InitializeAsync(timeout.Token);
        var cancelled = await fixture.Storage.PrepareAsync();
        await fixture.Storage.Actions.CancelActionAsync(cancelled.Owner, cancelled.Id, timeout.Token);
        var expired = await fixture.Storage.PrepareAsync(102);
        fixture.Storage.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var first = await fixture.Client.CallToolAsync(fixture.Request(cancelled), timeout.Token);
        var second = await fixture.Client.CallToolAsync(fixture.Request(expired), timeout.Token);

        // Assert
        Assert.Equal("action_cancelled", first.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("action_expired", second.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ExecuteReminder_IncorrectOffsetAndInjectedOwner_AreRejected()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var fixture = new ReminderMcpFixture();
        await fixture.InitializeAsync(timeout.Token);
        var action = await fixture.Storage.PrepareAsync();
        await fixture.Storage.Actions.ConfirmActionAsync(action.Owner, action.Id, AgentChecks.UpdateId(101), timeout.Token);
        var wrongOffset = fixture.Request(action);
        wrongOffset.Arguments = new Dictionary<string, JsonElement>(wrongOffset.Arguments!)
        {
            ["remind_at"] = JsonSerializer.SerializeToElement("2030-01-02T12:00:00+04:00"),
        };
        var injected = fixture.Request(action);
        injected.Arguments = new Dictionary<string, JsonElement>(injected.Arguments!)
        {
            ["owner_id"] = JsonSerializer.SerializeToElement(84),
        };

        // Act
        var offset = await fixture.Client.CallToolAsync(wrongOffset, timeout.Token);
        var extra = await fixture.Client.CallToolAsync(injected, timeout.Token);

        // Assert
        Assert.Equal("action_mismatch", offset.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("invalid_arguments", extra.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ExecuteReminder_StorageFailure_DoesNotReportSuccessOrExposeSql()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var fixture = new ReminderMcpFixture();
        await fixture.InitializeAsync(timeout.Token);
        var action = await fixture.Storage.PrepareAsync();
        await fixture.Storage.Actions.ConfirmActionAsync(action.Owner, action.Id, AgentChecks.UpdateId(101), timeout.Token);
        await using var drop = fixture.Storage.Storage.Source.CreateCommand("DROP TABLE reminders");
        await drop.ExecuteNonQueryAsync(timeout.Token);

        // Act
        var result = await fixture.Client.CallToolAsync(fixture.Request(action), timeout.Token);

        // Assert
        Assert.True(result.IsError);
        var value = result.StructuredContent!.Value;
        Assert.Equal("storage_unavailable", value.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("SELECT", value.GetRawText());
        Assert.False(value.TryGetProperty("reminder_id", out _));
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ExecuteReminder_BotAdapter_CreatesOverRealStdioAndRepeatsAfterServerRestartWithNewKey()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var fixture = new AgentRepositoryFixture();
        await fixture.InitializeAsync();
        fixture.Clock.Advance(DateTimeOffset.UtcNow - fixture.Clock.GetUtcNow());
        var action = await fixture.PrepareAsync();
        await fixture.Actions.ConfirmActionAsync(action.Owner, action.Id, AgentChecks.UpdateId(101), timeout.Token);
        var client = new StdioMcpClient(new McpTestSettings().Create(), new ReminderTestTransportFactory(fixture.Storage.CreateScopedConnectionString()),
            new McpErrorMapper(), TimeProvider.System, NullLogger.Instance, new McpCatalogReader(new McpCatalogMapper()));

        // Act
        ReminderCreation first;
        await using (var session = Assert.IsType<Result<IMcpSession>.Success>(await client.ConnectAsync(timeout.Token)).Value)
        {
            first = fixture.Value(await session.ExecuteReminderAsync(action, timeout.Token));
        }

        await using var restarted = Assert.IsType<Result<IMcpSession>.Success>(await client.ConnectAsync(timeout.Token)).Value;
        var repeated = fixture.Value(await restarted.ExecuteReminderAsync(action, timeout.Token));

        // Assert
        Assert.True(first.Created);
        Assert.False(repeated.Created);
        Assert.Equal(first.Reminder.Id, repeated.Reminder.Id);
    }

    [PostgresFact]
    [Trait("Category", "Integration")]
    public async Task ExecuteReminder_PastReminderTime_IsRejectedEvenWithFreshSignatureAndConfirmation()
    {
        // Arrange
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var fixture = new ReminderMcpFixture();
        await fixture.InitializeAsync(timeout.Token);
        var action = await fixture.Storage.PrepareAsync();
        await fixture.Storage.Actions.ConfirmActionAsync(action.Owner, action.Id, AgentChecks.UpdateId(101), timeout.Token);
        fixture.Storage.Clock.Advance(TimeSpan.FromDays(2));

        // Act
        var result = await fixture.Client.CallToolAsync(fixture.Request(action), timeout.Token);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("invalid_arguments", result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        await using var count = fixture.Storage.Storage.Source.CreateCommand("SELECT count(*) FROM reminders");
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

}
