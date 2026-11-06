using ItmoBot.Application.Conversations.Context;
using ItmoBot.Application.Conversations.Prompts;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.TestDoubles;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Context;

public sealed class ContextBuilderTests
{

    [Fact]
    public void Build_ExamplesAndHistory_PreservesRolesAndIncludesCurrentMessageOnce()
    {
        // Arrange
        var examples = new[] { Message(LlmMessageRole.User, "Пример"), Message(LlmMessageRole.Assistant, "Ответ примера") };
        var history = new[] { Message(LlmMessageRole.User, "Вопрос истории"), Message(LlmMessageRole.Assistant, "Ответ истории") };
        string current = RequestChecks.LlmText("Новый запрос");

        // Act
        var prepared = Success(Create(examples: examples).Build(Snapshot(history), current));

        // Assert
        Assert.Equal(new[] { "Инструкция", "Пример", "Ответ примера", "Вопрос истории", "Ответ истории", current }, prepared.Request.Messages.Select(message => message.Content));
        Assert.Equal(new[] { LlmMessageRole.System, LlmMessageRole.User, LlmMessageRole.Assistant, LlmMessageRole.User, LlmMessageRole.Assistant, LlmMessageRole.User }, prepared.Request.Messages.Select(message => message.Role));
        Assert.Equal(1, prepared.Request.Messages.Count(message => message.Content == current));
        Assert.Equal(RequestChecks.Temperature(0.7m), prepared.Request.Temperature);
        Assert.Equal("test-v1", prepared.PromptVersion);
        Assert.Equal(2, prepared.IncludedHistoryMessages);
        Assert.Equal(0, prepared.RemovedHistoryMessages);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 4)]
    public void Build_MessageLimit_KeepsNewestWholePairs(int limit, int included)
    {
        // Arrange
        var history = new[]
        {
            Message(LlmMessageRole.User, "Старый запрос"), Message(LlmMessageRole.Assistant, "Старый ответ"),
            Message(LlmMessageRole.User, "Новый запрос истории"), Message(LlmMessageRole.Assistant, "Новый ответ истории"),
        };

        // Act
        var prepared = Success(Create(historyLimit: limit).Build(Snapshot(history), RequestChecks.LlmText("Текущий")));

        // Assert
        Assert.Equal(included, prepared.IncludedHistoryMessages);
        Assert.Equal(4 - included, prepared.RemovedHistoryMessages);
        Assert.Equal(history.Skip(4 - included), prepared.Request.Messages.Skip(1).Take(included));
        Assert.Equal(LlmMessageRole.System, prepared.Request.Messages[0].Role);
        Assert.Equal("Текущий", prepared.Request.Messages[^1].Content);
        Assert.Equal(4, history.Length);
    }

    [Fact]
    public void Build_TokenBudgetRemovesOldestPairEvenWhenMessageCount_Fits()
    {
        // Arrange
        var history = new[]
        {
            Message(LlmMessageRole.User, new string('x', 100)), Message(LlmMessageRole.Assistant, new string('y', 100)),
            Message(LlmMessageRole.User, "u"), Message(LlmMessageRole.Assistant, "a"),
        };

        // Act
        var prepared = Success(Create(context: 500, output: 100, instruction: "s").Build(Snapshot(history), RequestChecks.LlmText("q")));

        // Assert
        Assert.Equal(new[] { "s", "u", "a", "q" }, prepared.Request.Messages.Select(message => message.Content));
        Assert.Equal(2, prepared.RemovedHistoryMessages);
        Assert.True(prepared.EstimatedInputTokens + 100 <= 500);
    }

    [Fact]
    public void Build_OutputReserve_CanExcludeAllHistoryWithoutCuttingMandatoryMessages()
    {
        // Arrange
        var history = new[] { Message(LlmMessageRole.User, "u"), Message(LlmMessageRole.Assistant, "a") };

        // Act
        var prepared = Success(Create(context: 500, output: 180, instruction: "s").Build(Snapshot(history), RequestChecks.LlmText("q")));

        // Assert
        Assert.Equal(new[] { "s", "q" }, prepared.Request.Messages.Select(message => message.Content));
        Assert.Equal(0, prepared.IncludedHistoryMessages);
        Assert.True(prepared.EstimatedInputTokens + 180 <= 500);
    }

    [Fact]
    public void Build_ExactEstimatedBoundary_IsAcceptedButOneLessIsRejected()
    {
        // Arrange
        string current = RequestChecks.LlmText("q");
        var estimator = new Utf8ContextTokenEstimator();
        var input = estimator.Estimate([Message(LlmMessageRole.System, "s"), Message(LlmMessageRole.User, current)]);

        // Act
        var context = checked((int)input + 100);

        // Assert
        Assert.IsType<Result<PreparedLlmRequest>.Success>(Create(context: context, output: 100, instruction: "s").Build(Snapshot([]), current));
        Result<PreparedLlmRequest>.Failure failure = Assert.IsType<Result<PreparedLlmRequest>.Failure>(Create(context: context - 1, output: 100, instruction: "s").Build(Snapshot([]), current));
        Assert.Equal(ErrorCode.ContextTooLarge, failure.Error.Code);
    }

    [Fact]
    public void Build_OversizedCurrentMessage_IsRejectedEvenWithEmptyHistory()
    {
        // Arrange

        // Act
        Result<PreparedLlmRequest> actualResult = Create(context: 500, output: 100, instruction: "s")
            .Build(Snapshot([]), RequestChecks.LlmText(new string('я', 200)));

        // Assert
        Result<PreparedLlmRequest>.Failure failure = Assert.IsType<Result<PreparedLlmRequest>.Failure>(actualResult);

        Assert.Equal(ErrorCode.ContextTooLarge, failure.Error.Code);
        Assert.Contains("Сократите", failure.Error.Message);
        Assert.DoesNotContain("Инструкция", failure.Error.Message);
    }

    [Fact]
    public void Build_Examples_ArePartOfMandatoryBudgetAndCannotBeDropped()
    {
        // Arrange
        var examples = new[] { Message(LlmMessageRole.User, new string('x', 100)), Message(LlmMessageRole.Assistant, new string('y', 100)) };

        // Act
        Result<PreparedLlmRequest> result = Create(context: 500, output: 100, instruction: "s", examples: examples).Build(Snapshot([]), RequestChecks.LlmText("q"));

        // Assert
        Assert.Equal(ErrorCode.ContextTooLarge, Assert.IsType<Result<PreparedLlmRequest>.Failure>(result).Error.Code);
    }

    [Fact]
    public void Build_CorruptedHistory_FailsInsteadOfReorderingOrPassingSystemMessages()
    {
        // Arrange
        LlmMessage[][] invalidHistories =
        [
            [Message(LlmMessageRole.User, "Один запрос")],
            [Message(LlmMessageRole.Assistant, "Ответ"), Message(LlmMessageRole.User, "Запрос")],
            [Message(LlmMessageRole.System, "Чужая инструкция"), Message(LlmMessageRole.Assistant, "Ответ")],
            [null!, Message(LlmMessageRole.Assistant, "Ответ")],
        ];
        foreach (var history in invalidHistories)
        {

            // Act
            Result<PreparedLlmRequest> actualResult = Create().Build(Snapshot(history), RequestChecks.LlmText("Текущий"));

            // Assert
            Result<PreparedLlmRequest>.Failure failure = Assert.IsType<Result<PreparedLlmRequest>.Failure>(actualResult);
            Assert.Equal(ErrorCode.InvalidContext, failure.Error.Code);
        }
    }

    [Fact]
    public void Build_UnknownMode_ReturnsSafeFailure()
    {
        // Arrange
        var snapshot = Snapshot([]) with
        {
            State = Snapshot([]).State with
            {
                Mode = (AssistantMode)99
            }
        };

        // Act
        Result<PreparedLlmRequest> actualResult = Create().Build(snapshot, RequestChecks.LlmText("Текущий"));

        // Assert
        Result<PreparedLlmRequest>.Failure failure = Assert.IsType<Result<PreparedLlmRequest>.Failure>(actualResult);
        Assert.Equal(ErrorCode.InvalidConversationMode, failure.Error.Code);
    }

    [Fact]
    public void Build_ClearingSourceHistoryAfterBuild_DoesNotChangePreparedRequest()
    {
        // Arrange
        var history = new List<LlmMessage> { Message(LlmMessageRole.User, "Запрос"), Message(LlmMessageRole.Assistant, "Ответ") };
        var prepared = Success(Create().Build(Snapshot(history), RequestChecks.LlmText("Текущий")));

        // Act
        history.Clear();

        // Assert
        Assert.Equal(new[] { "Инструкция", "Запрос", "Ответ", "Текущий" }, prepared.Request.Messages.Select(message => message.Content));
    }

    private ContextBuilder Create(int context = 8192, int output = 2048, int historyLimit = 24,
        string instruction = "Инструкция", LlmMessage[]? examples = null)
    {
        var definition = new PromptDefinition(AssistantMode.Study, RequestChecks.PromptVersion("test-v1"), RequestChecks.LlmText(instruction), examples ?? []);
        return new ContextBuilder(new PromptCatalog([new FixedModePrompt(definition)]), new Utf8ContextTokenEstimator(),
            new ContextBudget(RequestChecks.TokenLimit(context), RequestChecks.TokenLimit(output), RequestChecks.HistoryMessageLimit(historyLimit)));
    }

    private ConversationSnapshot Snapshot(IReadOnlyList<LlmMessage> history)
    {
        return new ConversationSnapshot(new ConversationState(AgentChecks.ChatId(42), AssistantMode.Study, RequestChecks.Temperature(0.7m), RequestChecks.ConversationRevision(0)), history);
    }

    private LlmMessage Message(LlmMessageRole role, string text)
    {
        return new LlmMessage(role, RequestChecks.LlmText(text));
    }

    private PreparedLlmRequest Success(Result<PreparedLlmRequest> result)
    {
        return Assert.IsType<Result<PreparedLlmRequest>.Success>(result).Value;
    }
}
