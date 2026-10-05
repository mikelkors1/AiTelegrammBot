using ItmoBot.Application.Contracts;
using ItmoBot.Application.Handlers;
using ItmoBot.Application.Dialogue;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Hosting.Composition;
using ItmoBot.Infrastructure.Llm;
using ItmoBot.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ItmoBot.Tests.Unit.Hosting;

public sealed class ApplicationCompositionTests
{
    [Fact]
    public async Task MainHandlerResolvesAsAiHandlerInsideAsyncScope()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        new ApplicationComposition(new TestValues().Settings()).Configure(builder);
        using var host = builder.Build();

        await using var scope = host.Services.CreateAsyncScope();

        // Act
        var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler>();

        // Assert
        Assert.IsType<AiMessageHandler>(handler);
    }

    [Fact]
    public void ContextBuilderResolvesWithConfiguredBudgetsAndAllModes()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        new ApplicationComposition(new TestValues().Settings(new Dictionary<string, string>
        {
            ["HISTORY_MAX_MESSAGES"] = "1",
        })).Configure(builder);
        using var host = builder.Build();
        var context = host.Services.GetRequiredService<IContextBuilder>();
        var history = new[]
        {
            new LlmMessage(LlmMessageRole.User, new LlmText("Предыдущий вопрос")),
            new LlmMessage(LlmMessageRole.Assistant, new LlmText("Предыдущий ответ")),
        };
        foreach (var mode in Enum.GetValues<AssistantMode>())
        {
            var snapshot = new ConversationSnapshot(new ConversationState(new ChatId(42), mode, Temperature.Zero, ConversationRevision.Zero), history);

            // Act
            var actualResult = context.Build(snapshot, new LlmText("Новый вопрос"));

            // Assert
            var prepared = Assert.IsType<ContextBuildResult.Success>(actualResult).Prepared;
            Assert.Equal(0, prepared.IncludedHistoryMessages.Value);
            Assert.Equal(2, prepared.RemovedHistoryMessages.Value);
            Assert.True(prepared.EstimatedInputTokens.Value + 2048 <= 8192);
        }
    }

    [Fact]
    public void LlmClientCanBeResolvedInsideScopeAndRootResolutionIsRejected()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        new ApplicationComposition(new TestValues().Settings()).Configure(builder);
        using var host = builder.Build();
        using var firstScope = host.Services.CreateScope();
        using var secondScope = host.Services.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<ILlmClient>();
        var repeated = firstScope.ServiceProvider.GetRequiredService<ILlmClient>();

        // Act
        var second = secondScope.ServiceProvider.GetRequiredService<ILlmClient>();

        // Assert
        Assert.IsType<RetryingLlmClient>(first);
        Assert.Same(first, repeated);
        Assert.NotSame(first, second);
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<ILlmClient>());
    }
}
