using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Prompts;

public sealed class PromptCatalog : IPromptCatalog
{
    private readonly IReadOnlyDictionary<AssistantMode, PromptDefinition> definitions;

    public PromptCatalog(IEnumerable<IModePrompt> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        definitions = prompts.ToDictionary(prompt => prompt.Definition.Mode, prompt => prompt.Definition);
    }

    public PromptResolutionResult Resolve(AssistantMode mode)
    {
        if (!definitions.TryGetValue(mode, out var definition))
        {
            return new PromptResolutionResult.Failure(new AppError(ErrorCode.InvalidConversationMode, "Для выбранного режима нет инструкции."));
        }

        return new PromptResolutionResult.Success(definition);
    }
}
