using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;

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
