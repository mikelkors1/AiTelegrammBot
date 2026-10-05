using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface ITelegramReplyFormatter
{
    IReadOnlyList<OutgoingTelegramMessage> Format(LlmText text);
}
