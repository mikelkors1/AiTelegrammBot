using ItmoBot.Application.Conversations.Prompts;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FixedModePrompt : IModePrompt
{
    public FixedModePrompt(PromptDefinition definition)
    {
        Definition = definition;
    }

    public PromptDefinition Definition
    {
        get;
    }
}
