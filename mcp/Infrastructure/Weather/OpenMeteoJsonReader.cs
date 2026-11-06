using System.Text.Json;

namespace ItmoBot.Mcp.Infrastructure.Weather;

public sealed class OpenMeteoJsonReader
{
    public void ValidateObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Ожидался объект данных Open-Meteo.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new JsonException("Повторяющиеся поля данных Open-Meteo.");
            }
        }

        if (value.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.False)
        {
            throw new JsonException("Open-Meteo вернул ошибку вместо данных.");
        }
    }

    public JsonElement ReadProperty(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property))
        {
            throw new JsonException("Отсутствует обязательное поле данных Open-Meteo.");
        }

        return property;
    }

    public string ReadString(JsonElement value, string name)
    {
        var property = ReadProperty(value, name);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Ожидалось строковое поле данных Open-Meteo.");
        }

        return property.GetString()!;
    }

    public string? ReadOptionalString(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Некорректное необязательное поле данных Open-Meteo.");
        }

        return string.IsNullOrWhiteSpace(property.GetString()) ? null : property.GetString();
    }

    public double ReadFiniteNumber(JsonElement value, string name)
    {
        var property = ReadProperty(value, name);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            throw new JsonException("Ожидалось конечное числовое поле данных Open-Meteo.");
        }

        return number;
    }

    public long ReadInteger(JsonElement value, string name)
    {
        var property = ReadProperty(value, name);
        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var number))
        {
            throw new JsonException("Ожидалось целочисленное поле данных Open-Meteo.");
        }

        return number;
    }
}
