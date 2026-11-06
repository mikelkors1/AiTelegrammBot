using System.Text.Json.Nodes;
using ItmoBot.Agent.Security;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Shared.Validation;
using ItmoBot.Tests.Support;
using ItmoBot.Tests.TestDoubles;
using Xunit;

namespace ItmoBot.Tests.Unit.Agent;

public sealed class ActionContextTests
{
    [Fact]
    public void VerifyContext_Signature_VerifiesOnlyWithConnectionKeyAndExpiresAfterSixtySeconds()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var key = new McpSigningKey();
        var codec = new ActionContextCodec();
        var context = new ActionContextSigner(key, codec, clock).Sign(new TrustedActionTestValues().Action(clock));
        var verifier = new ActionContextVerifier(key, codec, clock);

        // Act
        var valid = verifier.Verify(context);
        var otherConnection = new ActionContextVerifier(new McpSigningKey(), codec, clock).Verify(context);
        clock.Advance(TimeSpan.FromSeconds(60));
        var expired = verifier.Verify(context);

        // Assert
        Assert.True(valid);
        Assert.False(otherConnection);
        Assert.False(expired);
        Assert.Equal("[СКРЫТО]", key.ToString());
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("action")]
    [InlineData("arguments")]
    [InlineData("expiry")]
    [InlineData("version")]
    [InlineData("signature")]
    public void VerifyContext_ChangingAnySignedField_InvalidatesSignature(string field)
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var key = new McpSigningKey();
        var codec = new ActionContextCodec();
        var context = new ActionContextSigner(key, codec, clock).Sign(new TrustedActionTestValues().Action(clock));
        var changed = field switch
        {
            "owner" => context with { Owner = new ActionOwner(AgentChecks.OwnerId(84), AgentChecks.ChatId(84)) },
            "action" => context with { ActionId = AgentChecks.PreparedActionId(Guid.NewGuid()) },
            "arguments" => context with { ArgumentsHash = AgentChecks.ArgumentsHash(new string('A', 64)) },
            "expiry" => context with { ExpiresAt = context.ExpiresAt - 1 },
            "version" => context with { Version = 2 },
            _ => context with { Signature = "not-base64" },
        };

        // Act
        var accepted = new ActionContextVerifier(key, codec, clock).Verify(changed);

        // Assert
        Assert.False(accepted);
    }

    [Fact]
    public void VerifyContext_Metadata_RoundTripsAndRejectsExtraFieldsAndMalformedIdentity()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var key = new McpSigningKey();
        var context = new ActionContextSigner(key, new ActionContextCodec(), clock).Sign(new TrustedActionTestValues().Action(clock));
        var mapper = new ActionContextMetadata();
        var metadata = mapper.Encode(context);

        // Act
        var restored = mapper.Decode(metadata);
        var extra = mapper.Encode(context);
        extra["itmobot/action"]!["unknown"] = true;
        var malformed = mapper.Encode(context);
        malformed["itmobot/action"]!["owner_id"] = -1;

        // Assert
        Assert.Equal(context, restored);
        Assert.Null(mapper.Decode(extra));
        Assert.Null(mapper.Decode(malformed));
        Assert.Null(mapper.Decode(new JsonObject()));
        Assert.Null(mapper.Decode(null));
    }

    [Fact]
    public void VerifyContext_MissingKey_NeverAuthorizesAndLongLivedSignatureIsRejected()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var key = new McpSigningKey();
        var codec = new ActionContextCodec();
        var context = new ActionContextSigner(key, codec, clock).Sign(new TrustedActionTestValues().Action(clock)) with
        {
            ExpiresAt = clock.GetUtcNow().ToUnixTimeSeconds() + 61,
        };
        context = context with
        {
            Signature = Convert.ToBase64String(key.ComputeSignature(codec.Encode(context)))
        };

        // Act
        var noKey = new ActionContextVerifier(null, codec, clock).Verify(context);
        var longLived = new ActionContextVerifier(key, codec, clock).Verify(context);

        // Assert
        Assert.False(noKey);
        Assert.False(longLived);
    }
}
