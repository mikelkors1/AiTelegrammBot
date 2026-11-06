using ItmoBot.Application.Conversations.Context;
using ItmoBot.Application.Conversations.Prompts;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Shared.Results;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Context;

public sealed class PromptCatalogTests
{

    [Fact]
    public void Resolve_Catalog_SelectsDistinctInstructionsForEachMode()
    {
        // Arrange
        var catalog = new PromptCatalog([new StudyPrompt(), new TranslatePrompt(), new QuizPrompt()]);
        var modes = Enum.GetValues<AssistantMode>();

        // Act
        var definitions = modes.Select(mode =>
            Assert.IsType<Result<PromptDefinition>.Success>(catalog.Resolve(mode)).Value).ToArray();

        // Assert
        Assert.Equal(modes, definitions.Select(definition => definition.Mode));
        Assert.Equal(modes.Length, definitions.Select(definition => definition.Version).Distinct().Count());
        Assert.Equal(modes.Length, definitions.Select(definition => definition.Messages[0].Content).Distinct().Count());
        Assert.All(definitions, definition => Assert.Equal(LlmMessageRole.System, definition.Messages[0].Role));
    }

    [Fact]
    public void Resolve_Translate_HasNormalAndBoundaryExamplesWithCorrectRoles()
    {
        // Arrange

        // Act
        var definition = new TranslatePrompt().Definition;

        // Assert
        Assert.Equal(5, definition.Messages.Count);
        Assert.Equal(new[] { LlmMessageRole.System, LlmMessageRole.User, LlmMessageRole.Assistant, LlmMessageRole.User, LlmMessageRole.Assistant }, definition.Messages.Select(message => message.Role));
        Assert.Equal("На какой язык перевести текст?", definition.Messages[^1].Content);
    }

    [Fact]
    public void Resolve_InvalidExamplePairsAndVersions_AreRejectedAtConstruction()
    {
        // Arrange
        var user = new LlmMessage(LlmMessageRole.User, RequestChecks.LlmText("Текст"));

        // Act
        Action act = () => new PromptDefinition(AssistantMode.Study, RequestChecks.PromptVersion("test-v1"), RequestChecks.LlmText("Инструкция"), [user]);

        // Assert
        Assert.Throws<ArgumentException>(act);
        Assert.Throws<ArgumentException>(() => new PromptDefinition(AssistantMode.Study, RequestChecks.PromptVersion("test-v1"), RequestChecks.LlmText("Инструкция"), [user, user]));
        Assert.Throws<InputValidationException>(() => RequestChecks.PromptVersion(""));
        Assert.Throws<InputValidationException>(() => new ContextBudget(RequestChecks.TokenLimit(100), RequestChecks.TokenLimit(100), RequestChecks.HistoryMessageLimit(2)));
    }

    [Fact]
    public void Estimate_UnicodeAndMultipleMessages_IncludesUtf8AndFramingOverhead()
    {
        // Arrange
        Utf8ContextTokenEstimator estimator = new Utf8ContextTokenEstimator();
        var ascii = new LlmMessage(LlmMessageRole.User, RequestChecks.LlmText("a"));

        // Act
        var unicode = new LlmMessage(LlmMessageRole.User, RequestChecks.LlmText("😀"));

        // Assert
        Assert.True(estimator.Estimate([unicode]) > estimator.Estimate([ascii]));
        Assert.True(estimator.Estimate([ascii, ascii]) > estimator.Estimate([new LlmMessage(LlmMessageRole.User, RequestChecks.LlmText("aa"))]));
        Assert.True(estimator.Estimate([]) > 0);
    }
}
