using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed record class LlmMessage
{
    public LlmMessage(LlmMessageRole role, LlmText content)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        ArgumentNullException.ThrowIfNull(content);
        Role = role;
        Content = content;
    }

    public LlmMessageRole Role
    {
        get;
    }
    public LlmText Content
    {
        get;
    }
}
