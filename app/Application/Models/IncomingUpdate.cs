using ItmoBot.Application.ValueObjects;

namespace ItmoBot.Application.Models;

public sealed record class IncomingUpdate(UpdateId Id, ChatId? ChatId, TelegramText? Text);
