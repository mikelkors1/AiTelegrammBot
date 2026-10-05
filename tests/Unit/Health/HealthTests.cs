using ItmoBot.Application.ResultTypes;
using System.Net;
using ItmoBot.Infrastructure.Health;
using ItmoBot.Infrastructure.Health.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Health;

public sealed class HealthTests
{
    [Fact]
    public async Task HealthReturnsDatabaseFailureAndDetectsRecoveryAndStoppedPolling()
    {
        // Arrange
        var database = new FakeDatabase();
        var pending = new TaskCompletionSource();
        var state = new HealthState(database) { Initialized = true, PollingTask = pending.Task };

        // Act
        var actualResult = await state.CheckAsync(CancellationToken.None);

        // Assert
        Assert.IsType<HealthEvaluationResult.Success>(actualResult);
        database.Fails = true;
        var failure = Assert.IsType<HealthEvaluationResult.Failure>(await state.CheckAsync(CancellationToken.None));
        Assert.Equal(ErrorCode.DatabaseUnavailable, failure.Error.Code);
        Assert.False(failure.Report.DatabaseAvailable);
        database.Fails = false;
        Assert.IsType<HealthEvaluationResult.Success>(await state.CheckAsync(CancellationToken.None));
        pending.SetResult();
        var stopped = Assert.IsType<HealthEvaluationResult.Failure>(await state.CheckAsync(CancellationToken.None));
        Assert.Equal(ErrorCode.ApplicationNotReady, stopped.Error.Code);
        Assert.False(stopped.Report.PollingRunning);
    }

    [Fact]
    public async Task HttpContractReturns503Then200()
    {
        // Arrange
        var port = new TestValues().FreePort();
        var pending = new TaskCompletionSource();
        var state = new HealthState(new FakeDatabase());
        await using var server = new HealthServerFactory().Create(state, port);
        await server.StartAsync();
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false });
            using var checker = new ReadinessChecker();

            // Act
            var actualResult = (await http.GetAsync($"http://127.0.0.1:{port.Value}/health")).StatusCode;

            // Assert
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                actualResult);
            Assert.Equal(ErrorCode.ApplicationNotReady,
                Assert.IsType<ReadinessCheckResult.Failure>(await checker.CheckAsync(port)).Error.Code);
            state.Initialized = true;
            state.PollingTask = pending.Task;
            var response = await http.GetAsync($"http://127.0.0.1:{port.Value}/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("{\"status\":\"ok\",\"database\":\"ok\",\"polling\":\"running\"}", await response.Content.ReadAsStringAsync());
            Assert.IsType<ReadinessCheckResult.Success>(await checker.CheckAsync(port));
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
