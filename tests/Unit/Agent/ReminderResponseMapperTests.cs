using System.Text.Json;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Infrastructure.Mcp;
using ItmoBot.Shared.Errors;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ItmoBot.Tests.Unit.Agent;

public sealed class ReminderResponseMapperTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"reminder_id\":\"not-a-guid\",\"remind_at\":\"2030-01-01T12:00:00+03:00\",\"timezone\":\"Europe/Moscow\",\"created\":true}")]
    [InlineData("{\"reminder_id\":\"33333333-3333-3333-3333-333333333333\",\"remind_at\":\"2030-01-01T12:00:00+04:00\",\"timezone\":\"Asia/Tbilisi\",\"created\":true}")]
    public void Map_UnexpectedResult_IsRejectedWithoutExposingResponse(string json)
    {
        // Arrange
        var action = new TrustedActionTestValues().Action(new ManualTimeProvider());
        var result = new CallToolResult { StructuredContent = JsonSerializer.Deserialize<JsonElement>(json), Content = [], IsError = false };

        // Act
        var mapped = new ReminderResponseMapper().Map(result, action);

        // Assert
        var error = Assert.IsType<AgentStorageResult<ReminderCreation>.Failure>(mapped).Error;
        Assert.Equal(ErrorCode.InvalidMcpResponse, error.Code);
        Assert.DoesNotContain("33333333", error.Message);
    }

    [Fact]
    public void Map_ErrorCode_IsMappedWithoutCopyingUntrustedMessage()
    {
        // Arrange
        var action = new TrustedActionTestValues().Action(new ManualTimeProvider());
        var result = new CallToolResult
        {
            StructuredContent = JsonSerializer.Deserialize<JsonElement>("""
                {"status":"error","error":{"code":"action_mismatch","message":"untrusted-secret-message","retryable":false}}
                """),
            Content = [],
            IsError = true,
        };

        // Act
        var mapped = new ReminderResponseMapper().Map(result, action);

        // Assert
        var error = Assert.IsType<AgentStorageResult<ReminderCreation>.Failure>(mapped).Error;
        Assert.Equal(ErrorCode.ActionMismatch, error.Code);
        Assert.DoesNotContain("untrusted-secret-message", error.Message);
    }
}
