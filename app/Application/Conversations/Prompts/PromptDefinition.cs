using ItmoBot.Application.Llm;

namespace ItmoBot.Application.Conversations.Prompts;

public sealed class PromptDefinition
{
    public PromptDefinition(AssistantMode mode, string version, string instruction, IEnumerable<LlmMessage> examples)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(examples);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var snapshot = examples.ToArray();
        if (snapshot.Length % 2 != 0)
        {
            throw new ArgumentException("Примеры должны состоять из полных пар user/assistant.", nameof(examples));
        }

        for (var index = 0; index < snapshot.Length; index += 2)
        {
            if (snapshot[index] is null || snapshot[index + 1] is null
                || snapshot[index].Role != LlmMessageRole.User || snapshot[index + 1].Role != LlmMessageRole.Assistant)
            {
                throw new ArgumentException("В примерах должны чередоваться роли user и assistant.", nameof(examples));
            }
        }

        Mode = mode;
        Version = version;
        Messages = Array.AsReadOnly(new[] { new LlmMessage(LlmMessageRole.System, instruction) }.Concat(snapshot).ToArray());
    }

    public AssistantMode Mode
    {
        get;
    }
    public string Version
    {
        get;
    }
    public IReadOnlyList<LlmMessage> Messages
    {
        get;
    }
}
