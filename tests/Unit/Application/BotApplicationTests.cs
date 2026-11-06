using ItmoBot.Application.Health;
using ItmoBot.Application.Mcp;
using ItmoBot.Application.Messaging;
using ItmoBot.Application.Storage;
using ItmoBot.Application;
using ItmoBot.Hosting;
using ItmoBot.Infrastructure.Health;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ItmoBot.Tests.Unit.Application;

public sealed class BotApplicationTests
{
    [Fact]
    public async Task RunAsync_MigrationFailure_StopsBeforeTelegramAndClosesSession()
    {
        // Arrange
        var database = new FakeDatabase();
        var telegram = new FakeTelegram();
        var error = new AppError(ErrorCode.DatabaseMigrationMismatch, "История миграций не совпадает.");

        // Act
        var result = await RunOwnedAsync(() => database, () => telegram,
            NullLogger.Instance, new TestValues().FreePort(), CancellationToken.None, migrationError: error);

        // Assert
        Assert.Equal(error, Assert.IsType<Result.Failure>(result).Error);
        Assert.Equal(0, telegram.InitializeCalls);
        Assert.Equal(0, telegram.PollCalls);
        Assert.True(database.Disposed);
        Assert.True(telegram.Disposed);
    }

    [Fact]
    public async Task RunAsync_DatabaseFailure_StopsBeforeTelegramInitialization()
    {
        // Arrange
        var telegram = new FakeTelegram();

        // Act
        var result = await RunOwnedAsync(() => new FakeDatabase { Fails = true }, () => telegram,
            NullLogger.Instance, new TestValues().FreePort(), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.DatabaseUnavailable, Assert.IsType<Result.Failure>(result).Error.Code);
        Assert.Equal(0, telegram.InitializeCalls);
    }

    [Fact]
    public async Task RunAsync_InitializationFailure_ClosesResourcesAndPreventsPolling()
    {
        // Arrange
        var database = new FakeDatabase();
        var telegram = new FakeTelegram { InitializationFails = true };

        // Act
        var result = await RunOwnedAsync(() => database, () => telegram,
            NullLogger.Instance, new TestValues().FreePort(), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.TelegramUnavailable, Assert.IsType<Result.Failure>(result).Error.Code);
        Assert.Equal(0, telegram.PollCalls);
        Assert.True(database.Disposed);
        Assert.True(telegram.Disposed);
    }

    [Fact]
    public async Task RunAsync_CreationException_ReturnsSafeFailureAndClosesCreatedClient()
    {
        // Arrange
        var telegram = new FakeTelegram();

        // Act
        var result = await RunOwnedAsync(() => throw new IOException("secret-password"),
            () => telegram, NullLogger.Instance, new TestValues().FreePort(), CancellationToken.None);

        // Assert
        var failure = Assert.IsType<Result.Failure>(result);
        Assert.Equal(ErrorCode.UnexpectedFailure, failure.Error.Code);
        Assert.DoesNotContain("secret-password", failure.Message);
        Assert.True(telegram.Disposed);
    }

    [Fact]
    public async Task RunAsync_NonRetryablePollingFailure_StopsApplication()
    {
        // Arrange
        var telegram = new FakeTelegram
        {
            PollError = new AppError(ErrorCode.TelegramPollingConflict, "Другой процесс polling."),
        };

        // Act
        var result = await RunOwnedAsync(() => new FakeDatabase(), () => telegram,
            NullLogger.Instance, new TestValues().FreePort(), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.TelegramPollingConflict, Assert.IsType<Result.Failure>(result).Error.Code);
        Assert.Equal(1, telegram.PollCalls);
    }

    [Fact]
    public async Task RunAsync_OccupiedHealthPort_ReturnsFailureAndClosesResources()
    {
        // Arrange
        int port = new TestValues().FreePort();
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
        listener.Start();
        var database = new FakeDatabase();
        var telegram = new FakeTelegram();

        // Act
        var result = await RunOwnedAsync(() => database, () => telegram,
            NullLogger.Instance, port, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.HealthEndpointUnavailable, Assert.IsType<Result.Failure>(result).Error.Code);
        Assert.Equal(0, telegram.PollCalls);
        Assert.True(database.Disposed);
        Assert.True(telegram.Disposed);
    }

    [Fact]
    public async Task RunAsync_Cancellation_StopsPollingClosesResourcesAndHttpServer()
    {
        // Arrange
        var database = new FakeDatabase();
        var telegram = new FakeTelegram();
        int port = new TestValues().FreePort();
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = RunOwnedAsync(() => database, () => telegram,
            NullLogger.Instance, port, stopping.Token);
        await telegram.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stopping.Cancel();

        // Act
        var actualResult = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsType<Result.Success>(actualResult);
        Assert.True(database.Disposed);
        Assert.True(telegram.Disposed);
        Assert.True(telegram.PollCancelled);
        using var checker = new ReadinessChecker();
        Assert.IsType<Result.Failure>(await checker.CheckAsync(port));
    }
    [Fact]
    public async Task RunAsync_Polling_DispatchesUpdatesToHandlerAndAdvancesOffset()
    {
        // Arrange
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var telegram = new FakeTelegram
        {
            InitialUpdates = [new IncomingUpdate(AgentChecks.UpdateId(17), AgentChecks.ChatId(42), TelegramChecks.TelegramText("Привет"))],
        };
        var handler = new FakeMessageHandler(() =>
        {
        });

        // Act
        var running = RunOwnedAsync(() => new FakeDatabase(), () => telegram,
            NullLogger.Instance, new TestValues().FreePort(), stopping.Token, handler);
        await telegram.SecondPollStarted.Task.WaitAsync(stopping.Token);
        stopping.Cancel();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsType<Result.Success>(result);
        Assert.Equal(17, Assert.Single(handler.ReceivedUpdates));
        Assert.Equal(new[] { 0, 18 }, telegram.PollOffsets);
        Assert.Empty(telegram.Sent);
    }

    [Fact]
    public async Task RunAsync_PendingDialogue_ProcessesOtherChatAndCallbackWithoutDuplicatingUpdates()
    {
        // Arrange
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var independent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int independentCalls = 0;
        int firstCalls = 0;
        bool sameChatOvertook = false;
        bool acknowledgedPending = false;
        IncomingUpdate[] updates =
        [
            new(17, 42, "Долгий вопрос"),
            new(18, 42, "Следующий вопрос"),
            new(19, 84, "Другой чат"),
            new(20, 42, null, 42, new TelegramCallback("callback", "confirm", 1)),
        ];
        var telegram = new FakeTelegram
        {
            OnPoll = async (offset, token) =>
            {
                if (!release.Task.IsCompleted && offset > 17)
                {
                    acknowledgedPending = true;
                }

                await Task.Delay(10, token);
                return new Result<IReadOnlyList<IncomingUpdate>>.Success(updates.Where(update => update.Id >= offset).ToArray());
            },
        };
        var handler = new FakeMessageHandler(() => { })
        {
            OnHandle = async (update, token) =>
            {
                if (update.Id == 17)
                {
                    Interlocked.Increment(ref firstCalls);
                    await release.Task.WaitAsync(token);
                }
                else if (update.Id == 18)
                {
                    sameChatOvertook = !release.Task.IsCompleted;
                }
                else if (Interlocked.Increment(ref independentCalls) == 2)
                {
                    independent.TrySetResult();
                }

                return new Result<bool>.Success(true);
            },
        };
        var running = RunOwnedAsync(() => new FakeDatabase(), () => telegram,
            NullLogger.Instance, new TestValues().FreePort(), stopping.Token, handler);

        // Act
        try
        {
            await independent.Task.WaitAsync(stopping.Token);
            await Task.Delay(50, stopping.Token);
            release.TrySetResult();
            while (!telegram.PollOffsets.Contains(21))
            {
                await Task.Delay(10, stopping.Token);
            }
        }
        finally
        {
            stopping.Cancel();
        }

        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsType<Result.Success>(result);
        Assert.Equal(1, firstCalls);
        Assert.Equal(2, independentCalls);
        Assert.False(sameChatOvertook);
        Assert.False(acknowledgedPending);
        Assert.Equal(4, handler.ReceivedUpdates.Count);
    }

    [Fact]
    public async Task RunAsync_FatalPollingError_CancelsPendingHandlerBeforeClosingResources()
    {
        // Arrange
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        bool handlerCancelled = false;
        int polls = 0;
        var telegram = new FakeTelegram
        {
            OnPoll = (_, _) => Task.FromResult<Result<IReadOnlyList<IncomingUpdate>>>(++polls == 1
                ? new Result<IReadOnlyList<IncomingUpdate>>.Success([new IncomingUpdate(17, 42, "Вопрос")])
                : new Result<IReadOnlyList<IncomingUpdate>>.Failure(new AppError(ErrorCode.TelegramPollingConflict, "Конфликт polling"))),
        };
        var handler = new FakeMessageHandler(() => { })
        {
            OnHandle = async (_, token) =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    handlerCancelled = true;
                    throw;
                }

                return new Result<bool>.Success(true);
            },
        };

        // Act
        var result = await RunOwnedAsync(() => new FakeDatabase(), () => telegram,
            NullLogger.Instance, new TestValues().FreePort(), deadline.Token, handler).WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(ErrorCode.TelegramPollingConflict, Assert.IsType<Result.Failure>(result).Error.Code);
        Assert.True(handlerCancelled);
        Assert.True(telegram.Disposed);
    }

    private async Task<Result> RunOwnedAsync(
        Func<IDatabase> createDatabase,
        Func<ITelegramGateway> createTelegram,
        ILogger logger,
        int port,
        CancellationToken cancellationToken,
        IMessageHandler? handler = null,
        AppError? migrationError = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger>(logger);
        services.AddSingleton<IMcpClient>(new FakeMcpClient());
        services.AddScoped<IMcpRuntime, McpRuntime>();
        services.AddSingleton<IDatabaseMigrator>(new FakeDatabaseMigrator(migrationError));
        services.AddScoped<ITelegramGateway>(_ => createTelegram());
        services.AddScoped<IDatabase>(_ => createDatabase());
        services.AddScoped<IReadinessServer>(provider => new ReadinessServer(
            provider.GetRequiredService<IDatabase>(), port, logger));
        services.AddScoped<IBotApplication>(provider =>
        {
            var telegram = provider.GetRequiredService<ITelegramGateway>();
            return new BotApplication(provider.GetRequiredService<IDatabase>(), telegram, logger,
                provider.GetRequiredService<IReadinessServer>(), handler ?? new EchoHandler(telegram));
        });
        await using var serviceProvider = services.BuildServiceProvider();
        return await new BotHost(serviceProvider.GetRequiredService<IServiceScopeFactory>(), logger).RunAsync(cancellationToken);
    }
}
