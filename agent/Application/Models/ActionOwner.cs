namespace ItmoBot.Application.Agent.Models;

public sealed record class ActionOwner
{
    public ActionOwner(long ownerId, long chatId)
    {
        if (ownerId <= 0 || chatId != ownerId)
        {
            throw new ArgumentException("Действия с напоминаниями доступны только в личном чате владельца.");
        }

        OwnerId = ownerId;
        ChatId = chatId;
    }

    public long OwnerId
    {
        get;
    }

    public long ChatId
    {
        get;
    }
}
