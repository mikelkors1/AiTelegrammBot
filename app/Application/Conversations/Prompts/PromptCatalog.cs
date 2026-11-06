using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Conversations.Prompts;

public sealed class PromptCatalog
{
    private readonly IReadOnlyDictionary<AssistantMode, PromptDefinition> definitions;

    public PromptCatalog(IEnumerable<IModePrompt> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        definitions = prompts.ToDictionary(prompt => prompt.Definition.Mode, prompt => prompt.Definition);
    }

    public Result<PromptDefinition> Resolve(AssistantMode mode)
    {
        if (!definitions.TryGetValue(mode, out var definition))
        {
            return new Result<PromptDefinition>.Failure(new AppError(ErrorCode.InvalidConversationMode, "Для выбранного режима нет инструкции."));
        }

        return new Result<PromptDefinition>.Success(definition);
    }
}
