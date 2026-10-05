using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface ICodeBlockParser
{
    IReadOnlyList<ReplySegment> Parse(LlmText text);
}
