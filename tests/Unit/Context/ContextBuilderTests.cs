using ItmoBot.Application.Context;
using ItmoBot.Application.Models;
using ItmoBot.Application.Prompts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Context;

public sealed class ContextBuilderTests
{
    [Fact]
    public void PreservesRolesOrderExamplesAndCurrentMessageExactlyOnce()
    {
        // Arrange
        var examples = new[] { Message(LlmMessageRole.User, "Пример"), Message(LlmMessageRole.Assistant, "Ответ примера") };
        var history = new[] { Message(LlmMessageRole.User, "Вопрос истории"), Message(LlmMessageRole.Assistant, "Ответ истории") };
        var current = new LlmText("Новый запрос");

        // Act
        var prepared = Success(Create(examples: examples).Build(Snapshot(history), current));

        // Assert
        Assert.Equal(new[] { "Инструкция", "Пример", "Ответ примера", "Вопрос истории", "Ответ истории", current.Value }, prepared.Request.Messages.Select(message => message.Content.Value));
        Assert.Equal(new[] { LlmMessageRole.System, LlmMessageRole.User, LlmMessageRole.Assistant, LlmMessageRole.User, LlmMessageRole.Assistant, LlmMessageRole.User }, prepared.Request.Messages.Select(message => message.Role));
        Assert.Equal(1, prepared.Request.Messages.Count(message => message.Content == current));
        Assert.Equal(new Temperature(0.7m), prepared.Request.Temperature);
        Assert.Equal("test-v1", prepared.PromptVersion.Value);
        Assert.Equal(2, prepared.IncludedHistoryMessages.Value);
        Assert.Equal(0, prepared.RemovedHistoryMessages.Value);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 4)]
    public void MessageLimitKeepsNewestWholePairs(int limit, int included)
    {
        // Arrange
        var history = new[]
        {
            Message(LlmMessageRole.User, "Старый запрос"), Message(LlmMessageRole.Assistant, "Старый ответ"),
            Message(LlmMessageRole.User, "Новый запрос истории"), Message(LlmMessageRole.Assistant, "Новый ответ истории"),
        };

        // Act
        var prepared = Success(Create(historyLimit: limit).Build(Snapshot(history), new LlmText("Текущий")));

        // Assert
        Assert.Equal(included, prepared.IncludedHistoryMessages.Value);
        Assert.Equal(4 - included, prepared.RemovedHistoryMessages.Value);
        Assert.Equal(history.Skip(4 - included), prepared.Request.Messages.Skip(1).Take(included));
        Assert.Equal(LlmMessageRole.System, prepared.Request.Messages[0].Role);
        Assert.Equal("Текущий", prepared.Request.Messages[^1].Content.Value);
        Assert.Equal(4, history.Length);
    }

    [Fact]
    public void TokenBudgetRemovesOldestPairEvenWhenMessageCountFits()
    {
        // Arrange
        var history = new[]
        {
            Message(LlmMessageRole.User, new string('x', 100)), Message(LlmMessageRole.Assistant, new string('y', 100)),
            Message(LlmMessageRole.User, "u"), Message(LlmMessageRole.Assistant, "a"),
        };

        // Act
        var prepared = Success(Create(context: 500, output: 100, instruction: "s").Build(Snapshot(history), new LlmText("q")));

        // Assert
        Assert.Equal(new[] { "s", "u", "a", "q" }, prepared.Request.Messages.Select(message => message.Content.Value));
        Assert.Equal(2, prepared.RemovedHistoryMessages.Value);
        Assert.True(prepared.EstimatedInputTokens.Value + 100 <= 500);
    }

    [Fact]
    public void OutputReserveCanExcludeAllHistoryWithoutCuttingMandatoryMessages()
    {
        // Arrange
        var history = new[] { Message(LlmMessageRole.User, "u"), Message(LlmMessageRole.Assistant, "a") };

        // Act
        var prepared = Success(Create(context: 500, output: 180, instruction: "s").Build(Snapshot(history), new LlmText("q")));

        // Assert
        Assert.Equal(new[] { "s", "q" }, prepared.Request.Messages.Select(message => message.Content.Value));
        Assert.Equal(0, prepared.IncludedHistoryMessages.Value);
        Assert.True(prepared.EstimatedInputTokens.Value + 180 <= 500);
    }

    [Fact]
    public void ExactEstimatedBoundaryIsAcceptedButOneLessIsRejected()
    {
        // Arrange
        var current = new LlmText("q");
        var estimator = new Utf8ContextTokenEstimator();
        var input = estimator.Estimate([Message(LlmMessageRole.System, "s"), Message(LlmMessageRole.User, current.Value)]).Value;

        // Act
        var context = checked((int)input + 100);

        // Assert
        Assert.IsType<ContextBuildResult.Success>(Create(context: context, output: 100, instruction: "s").Build(Snapshot([]), current));
        var failure = Assert.IsType<ContextBuildResult.Failure>(Create(context: context - 1, output: 100, instruction: "s").Build(Snapshot([]), current));
        Assert.Equal(ErrorCode.ContextTooLarge, failure.Error.Code);
    }

    [Fact]
    public void OversizedCurrentMessageIsRejectedEvenWithEmptyHistory()
    {
        // Arrange

        // Act
        var actualResult = Create(context: 500, output: 100, instruction: "s")
            .Build(Snapshot([]), new LlmText(new string('я', 200)));

        // Assert
        var failure = Assert.IsType<ContextBuildResult.Failure>(actualResult);

        Assert.Equal(ErrorCode.ContextTooLarge, failure.Error.Code);
        Assert.Contains("Сократите", failure.Error.Message);
        Assert.DoesNotContain("Инструкция", failure.Error.Message);
    }

    [Fact]
    public void ExamplesArePartOfMandatoryBudgetAndCannotBeDropped()
    {
        // Arrange
        var examples = new[] { Message(LlmMessageRole.User, new string('x', 100)), Message(LlmMessageRole.Assistant, new string('y', 100)) };

        // Act
        var result = Create(context: 500, output: 100, instruction: "s", examples: examples).Build(Snapshot([]), new LlmText("q"));

        // Assert
        Assert.Equal(ErrorCode.ContextTooLarge, Assert.IsType<ContextBuildResult.Failure>(result).Error.Code);
    }

    [Fact]
    public void CorruptedHistoryFailsInsteadOfReorderingOrPassingSystemMessages()
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
            var actualResult = Create().Build(Snapshot(history), new LlmText("Текущий"));

            // Assert
            var failure = Assert.IsType<ContextBuildResult.Failure>(actualResult);
            Assert.Equal(ErrorCode.InvalidContext, failure.Error.Code);
        }
    }

    [Fact]
    public void UnknownModeReturnsSafeFailure()
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
        var actualResult = Create().Build(snapshot, new LlmText("Текущий"));

        // Assert
        var failure = Assert.IsType<ContextBuildResult.Failure>(actualResult);
        Assert.Equal(ErrorCode.InvalidConversationMode, failure.Error.Code);
    }

    [Fact]
    public void ClearingSourceHistoryAfterBuildDoesNotChangePreparedRequest()
    {
        // Arrange
        var history = new List<LlmMessage> { Message(LlmMessageRole.User, "Запрос"), Message(LlmMessageRole.Assistant, "Ответ") };
        var prepared = Success(Create().Build(Snapshot(history), new LlmText("Текущий")));

        // Act
        history.Clear();

        // Assert
        Assert.Equal(new[] { "Инструкция", "Запрос", "Ответ", "Текущий" }, prepared.Request.Messages.Select(message => message.Content.Value));
    }

    private ContextBuilder Create(int context = 8192, int output = 2048, int historyLimit = 24,
        string instruction = "Инструкция", LlmMessage[]? examples = null)
    {
        var definition = new PromptDefinition(AssistantMode.Study, new PromptVersion("test-v1"), new LlmText(instruction), examples ?? []);
        return new ContextBuilder(new PromptCatalog([new FixedModePrompt(definition)]), new Utf8ContextTokenEstimator(),
            new ContextBudget(new TokenLimit(context), new TokenLimit(output), new HistoryMessageLimit(historyLimit)));
    }

    private ConversationSnapshot Snapshot(IReadOnlyList<LlmMessage> history)
    {
        return new ConversationSnapshot(new ConversationState(new ChatId(42), AssistantMode.Study, new Temperature(0.7m), ConversationRevision.Zero), history);
    }

    private LlmMessage Message(LlmMessageRole role, string text)
    {
        return new LlmMessage(role, new LlmText(text));
    }

    private PreparedLlmRequest Success(ContextBuildResult result)
    {
        return Assert.IsType<ContextBuildResult.Success>(result).Prepared;
    }
}
