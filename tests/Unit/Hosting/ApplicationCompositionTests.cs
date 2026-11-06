using ItmoBot.Application.Conversations.Context;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Mcp;
using ItmoBot.Application.Messaging;
using ItmoBot.Hosting.Composition;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ItmoBot.Tests.Unit.Hosting;

public sealed class ApplicationCompositionTests
{
    [Fact]
    public async Task ResolveServices_McpRuntime_IsSharedWithinBotScopeAndIsolatedBetweenScopes()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        new ApplicationComposition(new TestValues().Settings()).Configure(builder);
        using var host = builder.Build();
        await using var firstScope = host.Services.CreateAsyncScope();
        await using var secondScope = host.Services.CreateAsyncScope();

        // Act
        var first = firstScope.ServiceProvider.GetRequiredService<IMcpRuntime>();
        var repeated = firstScope.ServiceProvider.GetRequiredService<IMcpRuntime>();
        var second = secondScope.ServiceProvider.GetRequiredService<IMcpRuntime>();
        var handler = firstScope.ServiceProvider.GetRequiredService<IMessageHandler>();

        // Assert
        Assert.IsType<AiMessageHandler>(handler);
        Assert.Same(first, repeated);
        Assert.NotSame(first, second);
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<IMcpRuntime>());
    }

    [Fact]
    public void ResolveServices_ContextBuilder_ResolvesWithConfiguredBudgetsAndAllModes()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        new ApplicationComposition(new TestValues().Settings(new Dictionary<string, string>
        {
            ["HISTORY_MAX_MESSAGES"] = "1",
        })).Configure(builder);
        using var host = builder.Build();
        var context = host.Services.GetRequiredService<ContextBuilder>();
        var history = new[]
        {
            new LlmMessage(LlmMessageRole.User, RequestChecks.LlmText("Предыдущий вопрос")),
            new LlmMessage(LlmMessageRole.Assistant, RequestChecks.LlmText("Предыдущий ответ")),
        };
        foreach (var mode in Enum.GetValues<AssistantMode>())
        {
            var snapshot = new ConversationSnapshot(new ConversationState(AgentChecks.ChatId(42), mode, RequestChecks.Temperature(0.0m), RequestChecks.ConversationRevision(0)), history);

            // Act
            Result<PreparedLlmRequest> actualResult = context.Build(snapshot, RequestChecks.LlmText("Новый вопрос"));

            // Assert
            var prepared = Assert.IsType<Result<PreparedLlmRequest>.Success>(actualResult).Value;
            Assert.Equal(0, prepared.IncludedHistoryMessages);
            Assert.Equal(2, prepared.RemovedHistoryMessages);
            Assert.True(prepared.EstimatedInputTokens + 2048 <= 8192);
        }
    }

    [Fact]
    public void ResolveServices_LlmClient_CanBeResolvedInsideScopeAndRootResolutionIsRejected()
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
