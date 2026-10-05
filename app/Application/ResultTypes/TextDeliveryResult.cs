using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.ResultTypes;

public abstract record class TextDeliveryResult
{
    private TextDeliveryResult()
    {
    }

    public sealed record class Success(MessagePartCount Parts) : TextDeliveryResult;
    public sealed record class Failure(AppError Error, MessagePartCount DeliveredParts) : TextDeliveryResult;
}
