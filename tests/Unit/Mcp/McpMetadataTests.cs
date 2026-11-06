using System.Text.Json;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Shared.Validation;
using ItmoBot.Validation;
using Xunit;

namespace ItmoBot.Tests.Unit.Mcp;

public sealed class McpMetadataTests
{
    [Fact]
    public void ValidateMetadata_Schema_OwnsJsonAfterSourceDocumentIsDisposed()
    {
        // Arrange
        var document = JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}}}""");

        // Act
        JsonElement schema = RequestChecks.McpObjectSchema(document.RootElement);
        document.Dispose();

        // Assert
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("city").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"type\":\"string\"}")]
    public void ValidateMetadata_Schema_RejectsNonObjectContracts(string json)
    {
        // Arrange
        using var document = JsonDocument.Parse(json);

        // Act
        var error = Assert.Throws<InputValidationException>(() => RequestChecks.McpObjectSchema(document.RootElement));

        // Assert
        Assert.Equal(ErrorCode.InvalidMcpResponse, error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad name")]
    [InlineData("tool\nname")]
    public void ValidateMetadata_ToolName_RejectsInvalidIdentifiers(string name)
    {
        // Arrange
        var input = name;

        // Act
        var error = Assert.Throws<InputValidationException>(() => AgentChecks.McpToolName(input));

        // Assert
        Assert.Equal(ErrorCode.InvalidMcpResponse, error.Code);
    }
}
