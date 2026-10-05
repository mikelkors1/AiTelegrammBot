using ItmoBot.Application.ValueObjects;
using ItmoBot.Application.Models;
using ItmoBot.Application;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.Handlers;
using ItmoBot.Hosting.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ItmoBot.Hosting;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Infrastructure.Health;
using ItmoBot.Infrastructure.Health.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Unit.Application;

public sealed class BotApplicationTests
{
    [Fact]
    public async Task MigrationFailureStopsBeforeTelegramAndClosesSession()
    {
        // Arrange
        var database = new FakeDatabase();
        var telegram = new FakeTelegram();
        var error = new AppError(ErrorCode.DatabaseMigrationMismatch, "История миграций не совпадает.");

        // Act
        var result = await RunOwnedAsync(() => database, () => telegram,
            NullLogger.Instance, new TestValues().FreePort(), CancellationToken.None, migrationError: error);

        // Assert
        Assert.Equal(error, Assert.IsType<BotExecutionResult.Failure>(result).Error);
        Assert.Equal(0, telegram.InitializeCalls);
        Assert.Equal(0, telegram.PollCalls);
        Assert.True(database.Disposed);
        Assert.True(telegram.Disposed);
    }

    [Fact]
    public async Task DatabaseFailureStopsBeforeTelegramInitialization()
    {
        // Arrange
        var telegram = new FakeTelegram();

        // Act
        var result = await RunOwnedAsync(() => new FakeDatabase { Fails = true }, () => telegram,
            NullLogger.Instance, new TestValues().FreePort(), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.DatabaseUnavailable, Assert.IsType<BotExecutionResult.Failure>(result).Error.Code);
        Assert.Equal(0, telegram.InitializeCalls);
    }

    [Fact]
    public async Task InitializationFailureClosesResourcesAndPreventsPolling()
    {
        // Arrange
        var database = new FakeDatabase();
        var telegram = new FakeTelegram { InitializationFails = true };

        // Act
        var result = await RunOwnedAsync(() => database, () => telegram,
            NullLogger.Instance, new TestValues().FreePort(), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.TelegramUnavailable, Assert.IsType<BotExecutionResult.Failure>(result).Error.Code);
        Assert.Equal(0, telegram.PollCalls);
        Assert.True(database.Disposed);
        Assert.True(telegram.Disposed);
    }

    [Fact]
    public async Task CreationExceptionReturnsSafeFailureAndClosesCreatedClient()
    {
        // Arrange
        var telegram = new FakeTelegram();

        // Act
        var result = await RunOwnedAsync(() => throw new IOException("secret-password"),
            () => telegram, NullLogger.Instance, new TestValues().FreePort(), CancellationToken.None);

        // Assert
        var failure = Assert.IsType<BotExecutionResult.Failure>(result);
        Assert.Equal(ErrorCode.UnexpectedFailure, failure.Error.Code);
        Assert.DoesNotContain("secret-password", failure.Message);
        Assert.True(telegram.Disposed);
    }

    [Fact]
    public async Task NonRetryablePollingFailureStopsApplication()
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
        Assert.Equal(ErrorCode.TelegramPollingConflict, Assert.IsType<BotExecutionResult.Failure>(result).Error.Code);
        Assert.Equal(1, telegram.PollCalls);
    }

    [Fact]
    public async Task OccupiedHealthPortReturnsFailureAndClosesResources()
    {
        // Arrange
        var port = new TestValues().FreePort();
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port.Value);
        listener.Start();
        var database = new FakeDatabase();
        var telegram = new FakeTelegram();

        // Act
        var result = await RunOwnedAsync(() => database, () => telegram,
            NullLogger.Instance, port, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.HealthEndpointUnavailable, Assert.IsType<BotExecutionResult.Failure>(result).Error.Code);
        Assert.Equal(0, telegram.PollCalls);
        Assert.True(database.Disposed);
        Assert.True(telegram.Disposed);
    }

    [Fact]
    public async Task CancellationStopsPollingClosesResourcesAndHttpServer()
    {
        // Arrange
        var database = new FakeDatabase();
        var telegram = new FakeTelegram();
        var port = new TestValues().FreePort();
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = RunOwnedAsync(() => database, () => telegram,
            NullLogger.Instance, port, stopping.Token);
        await telegram.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stopping.Cancel();

        // Act
        var actualResult = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsType<BotExecutionResult.Success>(actualResult);
        Assert.True(database.Disposed);
        Assert.True(telegram.Disposed);
        Assert.True(telegram.PollCancelled);
        using var checker = new ReadinessChecker();
        Assert.IsType<ReadinessCheckResult.Failure>(await checker.CheckAsync(port));
    }
    [Fact]
    public async Task ApplicationUsesInjectedHandlerInsteadOfConstructingEchoHandler()
    {
        // Arrange
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var telegram = new FakeTelegram
        {
            InitialUpdates = [new IncomingUpdate(new UpdateId(17), new ChatId(42), new TelegramText("Привет"))],
        };
        var handler = new FakeMessageHandler(stopping.Cancel);

        // Act
        var result = await RunOwnedAsync(() => new FakeDatabase(), () => telegram,
            NullLogger.Instance, new TestValues().FreePort(), stopping.Token, handler);

        // Assert
        Assert.IsType<BotExecutionResult.Success>(result);
        Assert.Equal(17, Assert.Single(handler.ReceivedUpdates));
        Assert.Empty(telegram.Sent);
    }

    private async Task<BotExecutionResult> RunOwnedAsync(
        Func<IDatabase> createDatabase,
        Func<ITelegramGateway> createTelegram,
        ILogger logger,
        ItmoBot.Shared.ValueObjects.PortNumber port,
        CancellationToken cancellationToken,
        IMessageHandler? handler = null,
        AppError? migrationError = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDatabaseMigrator>(new FakeDatabaseMigrator(migrationError));
        services.AddScoped<ITelegramGateway>(_ => createTelegram());
        services.AddScoped<IDatabase>(_ => createDatabase());
        services.AddScoped<IReadinessServer>(provider => new ReadinessServer(
            provider.GetRequiredService<IDatabase>(), port, new HealthServerFactory(), logger));
        services.AddScoped<IBotApplication>(provider =>
        {
            var telegram = provider.GetRequiredService<ITelegramGateway>();
            return new BotApplication(provider.GetRequiredService<IDatabase>(), telegram, logger,
                provider.GetRequiredService<IReadinessServer>(), handler ?? new EchoHandler(telegram));
        });
        await using var serviceProvider = services.BuildServiceProvider();
        var sessions = new BotSessionFactory(serviceProvider.GetRequiredService<IServiceScopeFactory>());
        return await new BotHost(sessions, logger).RunAsync(cancellationToken);
    }
}
