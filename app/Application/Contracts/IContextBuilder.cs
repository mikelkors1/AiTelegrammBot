using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IContextBuilder
{
    ContextBuildResult Build(ConversationSnapshot conversation, LlmText currentMessage);
}
