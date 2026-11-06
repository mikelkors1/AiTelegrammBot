using System.Text.Json;
using ItmoBot.Application.Mcp;
using ItmoBot.Mcp.Protocol;
using ItmoBot.Tests.Support;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Agent;

public sealed class ToolSchemaValidationTests
{
    [Fact]
    public void IsValid_RemoteReferences_AreRejectedWithoutNetwork()
    {
        // Arrange
        var fixture = new AgentLoopFixture();
        JsonElement schema = RequestChecks.McpObjectSchema(fixture.Json("{\"type\":\"object\",\"$ref\":\"http://127.0.0.1:1/private\"}"));

        // Act
        var valid = new ToolSchemaValidator().IsValid(schema, fixture.Json("{}"));

        // Assert
        Assert.False(valid);
    }

    [Fact]
    public void IsValid_WeatherSchema_ChecksOutputVariantsAndUnknownFields()
    {
        // Arrange
        var fixture = new AgentLoopFixture();
        JsonElement schema = RequestChecks.McpObjectSchema(WeatherMcpEndpoint.GetTool().OutputSchema!.Value);
        var validator = new ToolSchemaValidator();

        // Act
        var missing = validator.IsValid(schema, fixture.Json("{\"status\":\"not_found\"}"));
        var unexpected = validator.IsValid(schema, fixture.Json("{\"status\":\"not_found\",\"temperature_c\":25}"));

        // Assert
        Assert.True(missing);
        Assert.False(unexpected);
    }
}
