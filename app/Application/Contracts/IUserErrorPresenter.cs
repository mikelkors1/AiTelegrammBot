using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Application.Contracts;

public interface IUserErrorPresenter
{
    LlmText Format(AppError error);
    LlmText DeliveryFailure();
}
