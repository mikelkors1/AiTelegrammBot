using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Messaging;

public abstract record class TextDeliveryResult
{
    private TextDeliveryResult()
    {
    }

    public sealed record class Success(int Parts) : TextDeliveryResult;
    public sealed record class Failure(AppError Error, int DeliveredParts) : TextDeliveryResult;
}
