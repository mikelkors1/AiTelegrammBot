using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;

namespace ItmoBot.Application.ValueObjects;

public sealed record class Temperature
{
    public static readonly Temperature Zero = new(0.0m);

    public Temperature(decimal value)
    {
        if (value is not (0.0m or 0.3m or 0.7m or 1.0m))
        {
            throw new ValueObjectValidationException(ErrorCode.InvalidTemperature, "Коэффициент креативности ответа: допустимы 0.0, 0.3, 0.7, 1.0.");
        }

        Value = value;
    }

    public decimal Value
    {
        get;
    }

    public override string ToString()
    {
        return Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
