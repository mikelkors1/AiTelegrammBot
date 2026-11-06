using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Mcp;
using ItmoBot.Shared.Results;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Agent;

public sealed class AgentReplyParserTests
{
    [Theory]
    [InlineData("Просто текст")]
    [InlineData("{\"action\":\"respond\",\"text\":\"Ответ\"}")]
    [InlineData("{\"action\":\"execute\",\"text\":\"Ответ\",\"reason\":\"Причина\"}")]
    [InlineData("{\"action\":\"respond\",\"text\":\"Ответ\",\"reason\":\"Причина\",\"owner_id\":42}")]
    public void Parse_UnstructuredOrUnexpectedFinalReply_IsRejected(string json)
    {
        // Arrange
        var parser = new AgentReplyParser(new ToolSchemaValidator());

        // Act
        Result<AgentFinalReply> result = parser.Parse(RequestChecks.LlmText(json));

        // Assert
        Assert.IsType<Result<AgentFinalReply>.Failure>(result);
    }

    [Fact]
    public void Parse_Clarification_KeepsValidatedTextAndReason()
    {
        // Arrange
        var parser = new AgentReplyParser(new ToolSchemaValidator());
        string text = RequestChecks.LlmText("{\"action\":\"clarify\",\"text\":\"Во сколько?\",\"reason\":\"Не задано время\"}");

        // Act
        Result<AgentFinalReply> result = parser.Parse(text);

        // Assert
        var reply = Assert.IsType<Result<AgentFinalReply>.Success>(result).Value;
        Assert.True(reply.RequiresClarification);
        Assert.Equal("Во сколько?", reply.Text);
        Assert.Equal("Не задано время", reply.Reason);
    }
}
