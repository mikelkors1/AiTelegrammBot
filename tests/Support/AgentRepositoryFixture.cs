using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.Agent;
using ItmoBot.Infrastructure.PostgreSql.Agent;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace ItmoBot.Tests.Support;

internal sealed class AgentRepositoryFixture : IAsyncDisposable
{
    public AgentRepositoryFixture()
    {
        Storage = new PostgresTestScope();
        Clock = new ManualTimeProvider();
        Clock.Advance(new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero) - DateTimeOffset.UnixEpoch);
        Executor = new AgentTransactionExecutor(Storage.Source, NullLogger.Instance);
        var users = new AgentUserStore();
        var actions = new PreparedActionStore();
        Users = new PostgresAgentUserRepository(Executor, users, actions);
        Actions = new PostgresPreparedActionRepository(Executor, users, actions, new ReminderPayloadHasher(), new PreparedActionPolicy(), Clock);
        Reminders = new PostgresReminderRepository(Executor, users, actions, new ReminderPayloadHasher(), new PreparedActionPolicy(), Clock);
        Audit = new PostgresAgentAuditRepository(Executor, users);
    }

    public PostgresTestScope Storage
    {
        get;
    }
    public ManualTimeProvider Clock
    {
        get;
    }
    public AgentTransactionExecutor Executor
    {
        get;
    }
    public IAgentUserRepository Users
    {
        get;
    }
    public IPreparedActionRepository Actions
    {
        get;
    }
    public IReminderRepository Reminders
    {
        get;
    }
    public IAgentAuditRepository Audit
    {
        get;
    }

    public IReminderRepository CreateReminderRepository(NpgsqlDataSource source)
    {
        return new PostgresReminderRepository(new AgentTransactionExecutor(source, NullLogger.Instance),
            new AgentUserStore(), new PreparedActionStore(), new ReminderPayloadHasher(), new PreparedActionPolicy(), Clock);
    }

    public async Task InitializeAsync()
    {
        await Storage.InitializeAsync();
        Assert.IsType<Result<int>.Success>(await Storage.CreateMigrator().ApplyAsync(CancellationToken.None));
        await Users.SetTimeZoneAsync(AgentChecks.OwnerId(42), new UserTimeZone("Europe/Moscow"), CancellationToken.None);
    }

    public ActionOwner Owner(long id = 42)
    {
        return new ActionOwner(AgentChecks.OwnerId(id), AgentChecks.ChatId(id));
    }

    public ReminderPayload Payload(string text = "Проверить расписание")
    {
        var zone = new UserTimeZone("Europe/Moscow");
        return new ReminderPayload(AgentChecks.ReminderText(text), new ReminderSchedule(zone.ConvertInstant(Clock.GetUtcNow().AddDays(1)), zone));
    }

    public async Task<PreparedAction> PrepareAsync(int update = 100)
    {
        return Value(await Actions.PrepareReminderAsync(Owner(), AgentChecks.UpdateId(update), Payload(), CancellationToken.None));
    }

    public T Value<T>(AgentStorageResult<T> result)
    {
        return Assert.IsType<AgentStorageResult<T>.Success>(result).Value;
    }

    public async ValueTask DisposeAsync()
    {
        await Storage.DisposeAsync();
    }
}
