using ItmoBot.Application.Mcp;
using ItmoBot.Application.Storage;
using ItmoBot.Application;
using ItmoBot.Hosting;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ItmoBot.Tests.Unit.Hosting;

public sealed class BotSessionLifecycleTests
{
    [Fact]
    public async Task RunAsync_Mcp_IsPreparedAfterMigrationBeforeApplicationAndClosedAfterIt()
    {
        // Arrange
        var migrator = new FakeDatabaseMigrator();
        var session = new FakeMcpSession();
        var client = new FakeMcpClient
        {
            OnConnect = _ =>
            {
                Assert.Equal(1, migrator.ApplyCalls);
                return Task.FromResult<Result<IMcpSession>>(new Result<IMcpSession>.Success(session));
            },
        };
        await using var provider = Provider(client, migrator, async (runtime, token) =>
        {
            Assert.IsType<Result<McpCatalog>.Success>(await runtime.GetCatalogAsync(token));
            Assert.Equal(1, session.DiscoveryCalls);
            Assert.Equal(0, session.DisposeCalls);
            return new Result.Success();
        });

        // Act
        var result = await Host(provider).RunAsync(CancellationToken.None);

        // Assert
        Assert.IsType<Result.Success>(result);
        Assert.Equal(1, client.ConnectCalls);
        Assert.Equal(1, session.DisposeCalls);
    }

    [Fact]
    public async Task RunAsync_MigrationFailure_PreventsMcpConnectionAndApplicationStart()
    {
        // Arrange
        var client = new FakeMcpClient();
        var started = false;
        var error = new AppError(ErrorCode.DatabaseMigrationFailed, "Ошибка миграции.");
        await using var provider = Provider(client, new FakeDatabaseMigrator(error), (_, _) =>
        {
            started = true;
            return Task.FromResult<Result>(new Result.Success());
        });

        // Act
        var result = await Host(provider).RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(error, Assert.IsType<Result.Failure>(result).Error);
        Assert.Equal(0, client.ConnectCalls);
        Assert.False(started);
    }

    [Fact]
    public async Task RunAsync_McpFailure_DoesNotPreventApplicationStart()
    {
        // Arrange
        var client = new FakeMcpClient();
        var started = false;
        await using var provider = Provider(client, new FakeDatabaseMigrator(), async (runtime, token) =>
        {
            Assert.IsType<Result<McpCatalog>.Failure>(await runtime.GetCatalogAsync(token));
            started = true;
            return new Result.Success();
        });

        // Act
        var result = await Host(provider).RunAsync(CancellationToken.None);

        // Assert
        Assert.IsType<Result.Success>(result);
        Assert.True(started);
        Assert.Equal(1, client.ConnectCalls);
    }

    [Fact]
    public async Task RunAsync_ApplicationCancellation_ClosesMcpSession()
    {
        // Arrange
        var session = new FakeMcpSession();
        var client = new FakeMcpClient { Result = new Result<IMcpSession>.Success(session) };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = Provider(client, new FakeDatabaseMigrator(), async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new Result.Success();
        });
        using var cancellation = new CancellationTokenSource();
        var running = Host(provider).RunAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        cancellation.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, session.DisposeCalls);
    }

    private ServiceProvider Provider(FakeMcpClient client, FakeDatabaseMigrator migrator,
        Func<IMcpRuntime, CancellationToken, Task<Result>> run)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger>(NullLogger.Instance);
        services.AddSingleton<IMcpClient>(client);
        services.AddSingleton<IDatabaseMigrator>(migrator);
        services.AddScoped<IMcpRuntime, McpRuntime>();
        services.AddScoped<IBotApplication>(provider => new FakeBotApplication(token => run(provider.GetRequiredService<IMcpRuntime>(), token)));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private BotHost Host(ServiceProvider provider)
    {
        return new BotHost(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance);
    }
}
