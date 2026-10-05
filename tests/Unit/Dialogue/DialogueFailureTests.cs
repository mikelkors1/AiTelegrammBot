using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Dialogue;

public sealed class DialogueFailureTests
{
    [Fact]
    public async Task DatabaseReadFailureDoesNotCallLlm()
    {
        // Arrange
        var repository = new FakeConversationRepository { LoadError = new AppError(ErrorCode.DatabaseUnavailable, "db-secret") };
        var scenario = new BotScenario(repository);

        // Act
        var actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        var failure = Assert.IsType<MessageHandlingResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.DatabaseUnavailable, failure.Error.Code);
        Assert.Equal("Не удалось сохранить или прочитать диалог. Попробуйте позже.", Assert.Single(scenario.Telegram.Sent).Item2);
        Assert.Empty(scenario.Llm.Requests);
    }

    [Fact]
    public async Task BeginFailurePreventsProgressAndLlm()
    {
        // Arrange
        var repository = new FakeConversationRepository { BeginError = new AppError(ErrorCode.DatabaseOperationFailed, "db-secret") };
        var scenario = new BotScenario(repository);

        // Act
        var actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<MessageHandlingResult.Failure>(actualResult);
        Assert.Empty(scenario.Llm.Requests);
        Assert.Empty(repository.Turns);
        Assert.Single(scenario.Telegram.Sent);
    }

    [Fact]
    public async Task SavingFailureDoesNotSendUnsavedAnswer()
    {
        // Arrange
        var repository = new FakeConversationRepository { CompleteError = new AppError(ErrorCode.DatabaseOperationFailed, "db-secret") };
        var scenario = new BotScenario(repository);

        // Act
        var actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<MessageHandlingResult.Failure>(actualResult);
        Assert.Single(scenario.Llm.Requests);
        Assert.DoesNotContain(scenario.Telegram.Sent, message => message.Item2 == "Ответ модели");
        Assert.Equal(ConversationTurnStatus.Failed, Assert.Single(repository.Turns).Value.Status);
        Assert.Null(Assert.Single(repository.Turns).Value.Assistant);
    }

    [Fact]
    public async Task ProgressDeliveryFailurePreventsPaidGeneration()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Telegram.SendFailure = (_, _, call) => call == 1 ? new AppError(ErrorCode.TelegramUnavailable, "network-secret") : null;

        // Act
        var actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<MessageHandlingResult.Failure>(actualResult);
        Assert.Empty(scenario.Llm.Requests);
        Assert.Equal(ConversationTurnStatus.Failed, Assert.Single(((FakeConversationRepository)scenario.Repository).Turns).Value.Status);
        Assert.Empty(scenario.Telegram.Deleted);
    }

    [Fact]
    public async Task UnexpectedLlmExceptionProducesSafeFailureAndFailedAttempt()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.OnComplete = (_, _) => throw new IOException("secret-prompt-and-key");

        // Act
        var actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        var failure = Assert.IsType<MessageHandlingResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.UnexpectedFailure, failure.Error.Code);
        Assert.Equal(new MessageId(1), Assert.Single(scenario.Telegram.Deleted).MessageId);
        Assert.Equal("Не удалось обработать запрос. Попробуйте позже.", scenario.Telegram.Sent[^1].Item2);
        Assert.DoesNotContain("secret", failure.Message);
        Assert.Equal(ConversationTurnStatus.Failed, Assert.Single(((FakeConversationRepository)scenario.Repository).Turns).Value.Status);
    }

    [Fact]
    public async Task CallerCancellationMarksFailedAndReleasesChatLock()
    {
        // Arrange
        var scenario = new BotScenario();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Llm.OnComplete = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException();
        };
        using var stopping = new CancellationTokenSource();
        var running = scenario.SendAsync("Вопрос", cancellationToken: stopping.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stopping.Cancel();

        // Act
        Func<Task> act = () => running.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.Equal(new MessageId(1), Assert.Single(scenario.Telegram.Deleted).MessageId);
        Assert.Equal(ConversationTurnStatus.Failed, Assert.Single(((FakeConversationRepository)scenario.Repository).Turns).Value.Status);
        Assert.Equal("Готовлю ответ…", Assert.Single(scenario.Telegram.Sent).Item2);
        scenario.Llm.OnComplete = null;
        Assert.IsType<MessageHandlingResult.Success>(await scenario.SendAsync("Следующий").WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task SameChatResetWaitsForGenerationWhileAnotherChatCanProceed()
    {
        // Arrange
        var scenario = new BotScenario();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<LlmCompletionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Llm.OnComplete = (request, _) =>
        {
            if (request.Messages[^1].Content.Value == "Первый")
            {
                started.TrySetResult();
                return response.Task;
            }

            return Task.FromResult<LlmCompletionResult>(new LlmCompletionResult.Success(new LlmResponse(new LlmText("Другой ответ"), new ModelName("test"), null, null)));
        };
        var first = scenario.SendAsync("Первый", 42);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        var reset = scenario.SendAsync("/reset", 42);

        // Assert
        Assert.False(reset.IsCompleted);
        Assert.IsType<MessageHandlingResult.Success>(await scenario.SendAsync("Другой", 43).WaitAsync(TimeSpan.FromSeconds(5)));
        response.TrySetResult(new LlmCompletionResult.Success(new LlmResponse(new LlmText("Первый ответ"), new ModelName("test"), null, null)));
        await Task.WhenAll(first, reset).WaitAsync(TimeSpan.FromSeconds(5));
        var cleared = Assert.IsType<ConversationResult<ConversationSnapshot>.Success>(await scenario.Repository.LoadAsync(new ChatId(42), CancellationToken.None));
        var other = Assert.IsType<ConversationResult<ConversationSnapshot>.Success>(await scenario.Repository.LoadAsync(new ChatId(43), CancellationToken.None));
        Assert.Empty(cleared.Value.History);
        Assert.Equal(2, other.Value.History.Count);
    }

    [Fact]
    public async Task ExternalResetDuringLlmPreventsSendingStaleAnswer()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.OnComplete = async (_, token) =>
        {
            await scenario.Repository.ResetAsync(new ChatId(42), token);
            return new LlmCompletionResult.Success(new LlmResponse(new LlmText("Запоздалый ответ"), new ModelName("test"), null, null));
        };

        // Act
        var actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<MessageHandlingResult.Failure>(actualResult);
        Assert.DoesNotContain(scenario.Telegram.Sent, message => message.Item2 == "Запоздалый ответ");
    }
}
