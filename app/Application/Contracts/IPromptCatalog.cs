using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Application.Contracts;

public interface IPromptCatalog
{
    PromptResolutionResult Resolve(AssistantMode mode);
}
