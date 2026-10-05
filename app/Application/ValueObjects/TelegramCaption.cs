using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class TelegramCaption
{
    public TelegramCaption(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 1024)
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidPhotoCaption, "Подпись фото должна содержать от 1 до 1024 символов.");
        }

        Value = value;
    }

    public string Value
    {
        get;
    }
}
