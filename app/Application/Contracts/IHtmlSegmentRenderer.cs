using ItmoBot.Application.Models;
using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Contracts;

public interface IHtmlSegmentRenderer
{
    IReadOnlyList<TelegramText> Render(ReplySegment segment);
}
