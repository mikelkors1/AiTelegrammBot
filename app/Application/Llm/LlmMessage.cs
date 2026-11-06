namespace ItmoBot.Application.Llm;

public sealed record class LlmMessage
{
    public LlmMessage(LlmMessageRole role, string content, IReadOnlyList<LlmToolCall>? toolCalls = null, string? toolCallId = null)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        ArgumentNullException.ThrowIfNull(content);
        Role = role;
        if ((toolCalls is { Count: > 0 } && role != LlmMessageRole.Assistant)
            || (role == LlmMessageRole.Tool) != (toolCallId is not null))
        {
            throw new ArgumentException("Некорректная связь сообщения с инструментом.");
        }

        Content = ItmoBot.Validation.RequestChecks.LlmText(content);
        ToolCalls = Array.AsReadOnly((toolCalls ?? []).ToArray());
        ToolCallId = toolCallId;
    }

    public LlmMessageRole Role
    {
        get;
    }
    public string Content
    {
        get;
    }
    public IReadOnlyList<LlmToolCall> ToolCalls
    {
        get;
    }
    public string? ToolCallId
    {
        get;
    }
}
