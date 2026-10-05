using ItmoBot.Application.Contracts;
using ItmoBot.Application.Context;
using ItmoBot.Application.Models;
using ItmoBot.Application.Prompts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Exceptions;
using Xunit;

namespace ItmoBot.Tests.Unit.Context;

public sealed class PromptCatalogTests
{
    [Theory]
    [InlineData(AssistantMode.Study, "study-v3")]
    [InlineData(AssistantMode.Translate, "translate-v3")]
    [InlineData(AssistantMode.Quiz, "quiz-v3")]
    public void EachModeHasDistinctVersionAndFitsDefaultBudget(AssistantMode mode, string version)
    {
        // Arrange
        var catalog = new PromptCatalog([new StudyPrompt(), new TranslatePrompt(), new QuizPrompt()]);
        var snapshot = new ConversationSnapshot(new ConversationState(new ChatId(42), mode, Temperature.Zero, ConversationRevision.Zero), []);
        var builder = new ContextBuilder(catalog, new Utf8ContextTokenEstimator(), new ContextBudget(new TokenLimit(8192), new TokenLimit(2048), new HistoryMessageLimit(24)));

        // Act
        var actualResult = builder.Build(snapshot, new LlmText("Привет"));

        // Assert
        var prepared = Assert.IsType<ContextBuildResult.Success>(actualResult).Prepared;

        Assert.Equal(version, prepared.PromptVersion.Value);
        Assert.Equal(LlmMessageRole.System, prepared.Request.Messages[0].Role);
        Assert.Equal("Привет", prepared.Request.Messages[^1].Content.Value);
        Assert.True(prepared.EstimatedInputTokens.Value + 2048 <= 8192);
        Assert.Equal(0, prepared.IncludedHistoryMessages.Value);
    }

    [Fact]
    public void TranslateHasNormalAndBoundaryExamplesWithCorrectRoles()
    {
        // Arrange

        // Act
        var definition = new TranslatePrompt().Definition;

        // Assert
        Assert.Equal(5, definition.Messages.Count);
        Assert.Equal(new[] { LlmMessageRole.System, LlmMessageRole.User, LlmMessageRole.Assistant, LlmMessageRole.User, LlmMessageRole.Assistant }, definition.Messages.Select(message => message.Role));
        Assert.Equal("На какой язык перевести текст?", definition.Messages[^1].Content.Value);
    }

    [Fact]
    public void InvalidExamplePairsAndVersionsAreRejectedAtConstruction()
    {
        // Arrange
        var user = new LlmMessage(LlmMessageRole.User, new LlmText("Текст"));

        // Act
        Action act = () => new PromptDefinition(AssistantMode.Study, new PromptVersion("test-v1"), new LlmText("Инструкция"), [user]);

        // Assert
        Assert.Throws<ArgumentException>(act);
        Assert.Throws<ArgumentException>(() => new PromptDefinition(AssistantMode.Study, new PromptVersion("test-v1"), new LlmText("Инструкция"), [user, user]));
        Assert.Throws<ValueObjectValidationException>(() => new PromptVersion(""));
        Assert.Throws<ValueObjectValidationException>(() => new EstimatedTokenCount(-1));
        Assert.Throws<ValueObjectValidationException>(() => new HistoryMessageCount(-1));
        Assert.Throws<ValueObjectValidationException>(() => new ContextBudget(new TokenLimit(100), new TokenLimit(100), new HistoryMessageLimit(2)));
    }

    [Fact]
    public void EstimationAccountsForUnicodeAndMessageFraming()
    {
        // Arrange
        IContextTokenEstimator estimator = new Utf8ContextTokenEstimator();
        var ascii = new LlmMessage(LlmMessageRole.User, new LlmText("a"));

        // Act
        var unicode = new LlmMessage(LlmMessageRole.User, new LlmText("😀"));

        // Assert
        Assert.True(estimator.Estimate([unicode]).Value > estimator.Estimate([ascii]).Value);
        Assert.True(estimator.Estimate([ascii, ascii]).Value > estimator.Estimate([new LlmMessage(LlmMessageRole.User, new LlmText("aa"))]).Value);
        Assert.True(estimator.Estimate([]).Value > 0);
    }
}
