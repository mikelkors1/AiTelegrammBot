using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface ICommandReplyFormatter
{
    LlmText Welcome(ConversationState state);
    LlmText Settings(ConversationState state);
    LlmText ModeChanged(ConversationState state);
    LlmText Reset(ConversationState state);
}
