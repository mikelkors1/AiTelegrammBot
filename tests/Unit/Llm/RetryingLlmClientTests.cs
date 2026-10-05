using ItmoBot.Application.Dialogue;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ItmoBot.Tests.Unit.Llm;

public sealed class RetryingLlmClientTests
{
    [Fact]
    public void ConfigurationHasFiveMinuteDefaultAndAllowsTenMinutes()
    {
        // Arrange
        var defaults = new TestValues().Settings();

        // Act
        var tenMinutes = new TestValues().Settings(new Dictionary<string, string> { ["LLM_RETRY_WINDOW_SECONDS"] = "600" });

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(5), defaults.Llm.RetryWindow.Value);
        Assert.Equal(TimeSpan.FromMinutes(10), tenMinutes.Llm.RetryWindow.Value);
    }

    [Fact]
    public async Task DecoratorOwnsInnerClientAndDisposesItOnce()
    {
        // Arrange
        var inner = new FakeLlmClient();
        var client = new RetryingLlmClient(inner, new LlmRetryWindow(300), new ManualTimeProvider(), NullLogger.Instance);
        client.Dispose();

        // Act
        client.Dispose();

        // Assert
        Assert.Equal(1, inner.DisposeCalls);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CompleteAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task FailureThenSuccessRetriesSameRequestWithoutIntermediateUserError()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var scenario = new BotScenario(decorateClient: inner => new RetryingLlmClient(inner, new LlmRetryWindow(300), clock, NullLogger.Instance));
        scenario.Llm.Error = new AppError(ErrorCode.LlmUnavailable, "provider-secret");

        // Act
        var running = scenario.SendAsync("Вопрос");
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal("Готовлю ответ…", Assert.Single(scenario.Telegram.Sent).Item2);
        Assert.Empty(scenario.Telegram.Deleted);
        Assert.Equal(ConversationTurnStatus.Pending, Assert.Single(((FakeConversationRepository)scenario.Repository).Turns).Value.Status);

        // Arrange
        scenario.Llm.Error = null;

        // Act
        clock.Advance(TimeSpan.FromSeconds(5));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsType<MessageHandlingResult.Success>(result);
        Assert.Equal(2, scenario.Llm.Requests.Count);
        Assert.Equal(new MessageId(1), Assert.Single(scenario.Telegram.Deleted).MessageId);
        Assert.Same(scenario.Llm.Requests[0], scenario.Llm.Requests[1]);
        Assert.Equal(new[] { "Готовлю ответ…", "Ответ модели" }, scenario.Telegram.Sent.Select(message => message.Item2));
        var repository = (FakeConversationRepository)scenario.Repository;
        Assert.Equal(1, repository.BeginCalls);
        Assert.Equal(0, repository.FailedCalls);
        Assert.Equal(ConversationTurnStatus.Succeeded, Assert.Single(repository.Turns).Value.Status);
    }

    [Fact]
    public async Task BackoffGrowsAndRetriesAllExpectedFailureTypesUntilSuccess()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var inner = new FakeLlmClient { Error = new AppError(ErrorCode.LlmUnauthorized, "key-secret") };
        using var client = new RetryingLlmClient(inner, new LlmRetryWindow(300), clock, NullLogger.Instance);
        var running = client.CompleteAsync(Request(), CancellationToken.None);
        foreach (var seconds in new[] { 5, 10, 20, 30, 30 })
        {
            await clock.WaitForTimerAsync(TimeSpan.FromSeconds(seconds));
            clock.Advance(TimeSpan.FromSeconds(seconds));
        }

        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(30));
        inner.Error = null;
        clock.Advance(TimeSpan.FromSeconds(30));

        // Act
        var actualResult = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsType<LlmCompletionResult.Success>(actualResult);
        Assert.Equal(7, inner.Requests.Count);
    }

    [Fact]
    public async Task RetryAfterIsRespectedWithoutSendingAnEarlierAttempt()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var inner = new FakeLlmClient();
        inner.OnComplete = (_, _) => Task.FromResult<LlmCompletionResult>(new LlmCompletionResult.Failure(
            new AppError(ErrorCode.LlmRateLimited, "secret"), TimeSpan.FromSeconds(45)));
        using var client = new RetryingLlmClient(inner, new LlmRetryWindow(300), clock, NullLogger.Instance);
        var running = client.CompleteAsync(Request(), CancellationToken.None);
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(45));

        // Act
        clock.Advance(TimeSpan.FromSeconds(44));

        // Assert
        Assert.Single(inner.Requests);
        Assert.False(running.IsCompleted);
        inner.OnComplete = null;
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsType<LlmCompletionResult.Success>(await running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task ExhaustedBudgetReportsOneFinalErrorAndMarksOneAttemptFailed()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var scenario = new BotScenario(decorateClient: inner => new RetryingLlmClient(inner, new LlmRetryWindow(300), clock, NullLogger.Instance));
        scenario.Llm.Error = new AppError(ErrorCode.LlmUnavailable, "secret");
        var running = scenario.SendAsync("Вопрос");
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(300));

        // Act
        var actualResult = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        var failure = Assert.IsType<MessageHandlingResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.LlmRetryTimeout, failure.Error.Code);
        Assert.Equal(new MessageId(1), Assert.Single(scenario.Telegram.Deleted).MessageId);
        Assert.Equal(new[] { "Готовлю ответ…", "Модель не ответила за отведённое время. Попробуйте позже." }, scenario.Telegram.Sent.Select(message => message.Item2));
        var repository = (FakeConversationRepository)scenario.Repository;
        Assert.Equal(1, repository.BeginCalls);
        Assert.Equal(1, repository.FailedCalls);
        Assert.Null(Assert.Single(repository.Turns).Value.Assistant);
    }

    [Fact]
    public async Task OverallDeadlineInterruptsEvenAnUncooperativeInFlightClient()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var response = new TaskCompletionSource<LlmCompletionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new FakeLlmClient { OnComplete = (_, _) => response.Task };
        using var client = new RetryingLlmClient(inner, new LlmRetryWindow(300), clock, NullLogger.Instance);

        // Act
        var running = client.CompleteAsync(Request(), CancellationToken.None);

        // Assert
        Assert.Single(inner.Requests);
        clock.Advance(TimeSpan.FromSeconds(300));

        Assert.Equal(ErrorCode.LlmRetryTimeout, Assert.IsType<LlmCompletionResult.Failure>(await running.WaitAsync(TimeSpan.FromSeconds(5))).Error.Code);
        response.TrySetResult(new LlmCompletionResult.Success(new LlmResponse(new LlmText("Поздний"), new ModelName("test"), null, null)));
    }

    [Fact]
    public async Task RetryAfterBeyondBudgetWaitsOnlyUntilDeadline()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var inner = new FakeLlmClient
        {
            OnComplete = (_, _) => Task.FromResult<LlmCompletionResult>(new LlmCompletionResult.Failure(
                new AppError(ErrorCode.LlmRateLimited, "secret"), TimeSpan.FromDays(1))),
        };
        using var client = new RetryingLlmClient(inner, new LlmRetryWindow(300), clock, NullLogger.Instance);
        var running = client.CompleteAsync(Request(), CancellationToken.None);
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(300));
        clock.Advance(TimeSpan.FromSeconds(300));

        // Act
        var actualResult = await running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsType<LlmCompletionResult.Failure>(actualResult);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task CallerCancellationDuringPauseStopsImmediatelyAndReleasesChat()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var scenario = new BotScenario(decorateClient: inner => new RetryingLlmClient(inner, new LlmRetryWindow(300), clock, NullLogger.Instance));
        scenario.Llm.Error = new AppError(ErrorCode.LlmUnavailable, "secret");
        using var stopping = new CancellationTokenSource();
        var running = scenario.SendAsync("Вопрос", cancellationToken: stopping.Token);
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(5));
        stopping.Cancel();

        // Act
        Func<Task> act = () => running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.Single(scenario.Llm.Requests);
        Assert.Single(scenario.Telegram.Sent);
        scenario.Llm.Error = null;
        Assert.IsType<MessageHandlingResult.Success>(await scenario.SendAsync("Следующий").WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private LlmRequest Request()
    {
        return new LlmRequest([new LlmMessage(LlmMessageRole.User, new LlmText("Вопрос"))], new Temperature(0.3m));
    }
}
