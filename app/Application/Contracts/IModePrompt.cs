using ItmoBot.Application.Models;

namespace ItmoBot.Application.Contracts;

public interface IModePrompt
{
    PromptDefinition Definition
    {
        get;
    }
}
