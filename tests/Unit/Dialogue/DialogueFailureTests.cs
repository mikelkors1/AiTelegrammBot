using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Dialogue;

public sealed class DialogueFailureTests
{
    [Fact]
    public async Task RespondAsync_DatabaseReadFailure_DoesNotCallLlm()
    {
        // Arrange
        var repository = new FakeConversationRepository { LoadError = new AppError(ErrorCode.DatabaseUnavailable, "db-secret") };
        var scenario = new BotScenario(repository);

        // Act
        Result<bool> actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Result<bool>.Failure failure = Assert.IsType<Result<bool>.Failure>(actualResult);
        Assert.Equal(ErrorCode.DatabaseUnavailable, failure.Error.Code);
        Assert.Equal("Не удалось сохранить или прочитать диалог. Попробуйте позже.", Assert.Single(scenario.Telegram.Sent).Item2);
        Assert.Empty(scenario.Llm.Requests);
    }

    [Fact]
    public async Task RespondAsync_BeginFailure_PreventsProgressAndLlm()
    {
        // Arrange
        var repository = new FakeConversationRepository { BeginError = new AppError(ErrorCode.DatabaseOperationFailed, "db-secret") };
        var scenario = new BotScenario(repository);

        // Act
        Result<bool> actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<Result<bool>.Failure>(actualResult);
        Assert.Empty(scenario.Llm.Requests);
        Assert.Empty(repository.Turns);
        Assert.Single(scenario.Telegram.Sent);
    }

    [Fact]
    public async Task RespondAsync_SavingFailure_DoesNotSendUnsavedAnswer()
    {
        // Arrange
        var repository = new FakeConversationRepository { CompleteError = new AppError(ErrorCode.DatabaseOperationFailed, "db-secret") };
        var scenario = new BotScenario(repository);

        // Act
        Result<bool> actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<Result<bool>.Failure>(actualResult);
        Assert.Single(scenario.Llm.Requests);
        Assert.DoesNotContain(scenario.Telegram.Sent, message => message.Item2 == "Ответ модели");
        Assert.Equal(ConversationTurnStatus.Failed, Assert.Single(repository.Turns).Value.Status);
        Assert.Null(Assert.Single(repository.Turns).Value.Assistant);
    }

    [Fact]
    public async Task RespondAsync_ProgressDeliveryFailure_PreventsPaidGeneration()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Telegram.SendFailure = (_, _, call) => call == 1 ? new AppError(ErrorCode.TelegramUnavailable, "network-secret") : null;

        // Act
        Result<bool> actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<Result<bool>.Failure>(actualResult);
        Assert.Empty(scenario.Llm.Requests);
        Assert.Equal(ConversationTurnStatus.Failed, Assert.Single(((FakeConversationRepository)scenario.Repository).Turns).Value.Status);
        Assert.Empty(scenario.Telegram.Deleted);
    }

    [Fact]
    public async Task RespondAsync_UnexpectedLlmException_ProducesSafeFailureAndFailedAttempt()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.OnComplete = (_, _) => throw new IOException("secret-prompt-and-key");

        // Act
        Result<bool> actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Result<bool>.Failure failure = Assert.IsType<Result<bool>.Failure>(actualResult);
        Assert.Equal(ErrorCode.UnexpectedFailure, failure.Error.Code);
        Assert.Equal(TelegramChecks.MessageId(1), Assert.Single(scenario.Telegram.Deleted).MessageId);
        Assert.Equal("Не удалось обработать запрос. Попробуйте позже.", scenario.Telegram.Sent[^1].Item2);
        Assert.DoesNotContain("secret", failure.Message);
        Assert.Equal(ConversationTurnStatus.Failed, Assert.Single(((FakeConversationRepository)scenario.Repository).Turns).Value.Status);
    }

    [Fact]
    public async Task RespondAsync_CallerCancellation_MarksTurnFailedAndReleasesLock()
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
        Assert.Equal(TelegramChecks.MessageId(1), Assert.Single(scenario.Telegram.Deleted).MessageId);
        Assert.Equal(ConversationTurnStatus.Failed, Assert.Single(((FakeConversationRepository)scenario.Repository).Turns).Value.Status);
        Assert.Equal("Готовлю ответ…", Assert.Single(scenario.Telegram.Sent).Item2);
        scenario.Llm.OnComplete = null;
        Assert.IsType<Result<bool>.Success>(await scenario.SendAsync("Следующий").WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task RespondAsync_SameChatReset_WaitsForGenerationWhileAnotherChatCanProceed()
    {
        // Arrange
        var scenario = new BotScenario();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<LlmCompletionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Llm.OnComplete = (request, _) =>
        {
            if (request.Messages[^1].Content == "Первый")
            {
                started.TrySetResult();
                return response.Task;
            }

            return Task.FromResult<LlmCompletionResult>(new LlmCompletionResult.Success(new LlmResponse(RequestChecks.LlmText("Другой ответ"), RequestChecks.ModelName("test"), null, null)));
        };
        var first = scenario.SendAsync("Первый", 42);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        var reset = scenario.SendAsync("/reset", 42);

        // Assert
        Assert.False(reset.IsCompleted);
        Assert.IsType<Result<bool>.Success>(await scenario.SendAsync("Другой", 43).WaitAsync(TimeSpan.FromSeconds(5)));
        response.TrySetResult(new LlmCompletionResult.Success(new LlmResponse(RequestChecks.LlmText("Первый ответ"), RequestChecks.ModelName("test"), null, null)));
        await Task.WhenAll(first, reset).WaitAsync(TimeSpan.FromSeconds(5));
        var cleared = Assert.IsType<Result<ConversationSnapshot>.Success>(await scenario.Repository.LoadAsync(AgentChecks.ChatId(42), CancellationToken.None));
        var other = Assert.IsType<Result<ConversationSnapshot>.Success>(await scenario.Repository.LoadAsync(AgentChecks.ChatId(43), CancellationToken.None));
        Assert.Empty(cleared.Value.History);
        Assert.Equal(2, other.Value.History.Count);
    }

    [Fact]
    public async Task RespondAsync_ExternalResetDuringLlm_PreventsSendingStaleAnswer()
    {
        // Arrange
        var scenario = new BotScenario();
        scenario.Llm.OnComplete = async (_, token) =>
        {
            await scenario.Repository.ResetAsync(AgentChecks.ChatId(42), token);
            return new LlmCompletionResult.Success(new LlmResponse(RequestChecks.LlmText("Запоздалый ответ"), RequestChecks.ModelName("test"), null, null));
        };

        // Act
        Result<bool> actualResult = await scenario.SendAsync("Вопрос");

        // Assert
        Assert.IsType<Result<bool>.Failure>(actualResult);
        Assert.DoesNotContain(scenario.Telegram.Sent, message => message.Item2 == "Запоздалый ответ");
    }
}
