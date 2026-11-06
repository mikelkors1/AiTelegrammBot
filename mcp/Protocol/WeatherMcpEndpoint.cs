using System.Globalization;
using System.Text.Json;
using ItmoBot.Mcp.Validation;
using ItmoBot.Mcp.Weather.Contracts;
using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;

namespace ItmoBot.Mcp.Protocol;

public sealed class WeatherMcpEndpoint(IWeatherService weather)
{
    public async ValueTask<CallToolResult> CallAsync(CallToolRequestParams? request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request?.Name != "get_weather")
        {
            throw new McpProtocolException("Неизвестный инструмент.", McpErrorCode.InvalidParams);
        }

        var arguments = request.Arguments;
        if (arguments is null || arguments.Count != 1 || !arguments.TryGetValue("city", out var city)
            || city.ValueKind != JsonValueKind.String)
        {
            return InvalidArguments();
        }

        string query;
        try
        {
            query = WeatherChecks.CityQuery(city.GetString()!);
        }
        catch (ArgumentException)
        {
            return InvalidArguments();
        }

        return Format(await weather.GetCurrentWeatherByCityAsync(query, cancellationToken));
    }

    private CallToolResult InvalidArguments()
    {
        return Format(new WeatherResult.Failure(new WeatherError(WeatherErrorCode.InvalidArguments,
            "Передайте только поле city: название города от 1 до 100 символов без управляющих символов. При необходимости уточните страну через запятую.", false)));
    }

    public static Tool GetTool()
    {
        return new Tool
        {
            Name = "get_weather",
            Description = "Текущая погода в городе по Open-Meteo, не прогноз на весь день. city — название города, при необходимости со страной/областью через запятую. При ambiguous попросите уточнение, не выбирайте первый вариант. weather_code — код условий WMO.",
            InputSchema = Schema("""
                {"type":"object","properties":{"city":{"type":"string","minLength":1,"maxLength":100}},"required":["city"],"additionalProperties":false}
                """),
            OutputSchema = Schema("""
                {
                  "type":"object",
                  "oneOf":[
                    {"type":"object","properties":{
                      "status":{"const":"ok"},"city":{"type":"string","minLength":1},
                      "country":{"type":"string","minLength":1},
                      "temperature_c":{"type":"number","minimum":-273.15},
                      "weather_code":{"type":"integer","enum":[0,1,2,3,45,48,51,53,55,56,57,61,63,65,66,67,71,73,75,77,80,81,82,85,86,95,96,97,99]},
                      "wind_speed":{"type":"number","minimum":0},"wind_unit":{"const":"km/h"},
                      "observed_at":{"type":"string","format":"date-time"},
                      "timezone":{"type":"string","minLength":1},"source":{"const":"open_meteo"}},
                      "required":["status","city","country","temperature_c","weather_code","wind_speed","wind_unit","observed_at","timezone","source"],"additionalProperties":false},
                    {"type":"object","properties":{"status":{"const":"not_found"}},"required":["status"],"additionalProperties":false},
                    {"type":"object","properties":{
                      "status":{"const":"ambiguous"},"candidates":{"type":"array","minItems":2,"maxItems":100,"items":{
                        "type":"object","properties":{
                          "city":{"type":"string","minLength":1},"country":{"type":"string","minLength":1},
                          "country_code":{"type":"string","pattern":"^[A-Z]{2}$"},"admin1":{"type":["string","null"]}},
                        "required":["city","country","country_code","admin1"],"additionalProperties":false}}},
                      "required":["status","candidates"],"additionalProperties":false},
                    {"type":"object","properties":{
                      "status":{"const":"error"},"error":{"type":"object","properties":{
                        "code":{"enum":["invalid_arguments","source_unavailable","external_timeout","invalid_external_response"]},
                        "message":{"type":"string","minLength":1},"retryable":{"type":"boolean"}},
                        "required":["code","message","retryable"],"additionalProperties":false}},
                      "required":["status","error"],"additionalProperties":false}
                  ]
                }
                """),
            Annotations = new ToolAnnotations { ReadOnlyHint = true, DestructiveHint = false, OpenWorldHint = true },
        };
    }

    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private CallToolResult Format(WeatherResult result)
    {
        var value = result switch
        {
            WeatherResult.Success success => FormatWeather(success.Weather),
            WeatherResult.NotFound => JsonSerializer.SerializeToElement(new { status = "not_found" }),
            WeatherResult.Ambiguous ambiguous => JsonSerializer.SerializeToElement(new
            {
                status = "ambiguous",
                candidates = ambiguous.Candidates.Select(location => new
                {
                    city = location.CityName,
                    country = location.CountryName,
                    country_code = location.CountryCode,
                    admin1 = location.AdministrativeArea,
                }),
            }),
            WeatherResult.Failure failure => FormatError(failure.Error),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        return new CallToolResult
        {
            StructuredContent = value,
            Content = [new TextContentBlock { Text = value.GetRawText() }],
            IsError = result is WeatherResult.Failure,
        };
    }

    private JsonElement FormatWeather(CurrentWeather weather)
    {
        return JsonSerializer.SerializeToElement(new
        {
            status = "ok",
            city = weather.Location.CityName,
            country = weather.Location.CountryName,
            temperature_c = weather.Temperature,
            weather_code = weather.ConditionCode,
            wind_speed = weather.WindSpeed,
            wind_unit = "km/h",
            observed_at = weather.ObservedAt.ToString("O", CultureInfo.InvariantCulture),
            timezone = weather.TimeZone.Value.Id,
            source = "open_meteo",
        });
    }

    private JsonElement FormatError(WeatherError error)
    {
        return JsonSerializer.SerializeToElement(new
        {
            status = "error",
            error = new
            {
                code = error.Code switch
                {
                    WeatherErrorCode.InvalidArguments => "invalid_arguments",
                    WeatherErrorCode.SourceUnavailable => "source_unavailable",
                    WeatherErrorCode.ExternalTimeout => "external_timeout",
                    WeatherErrorCode.InvalidExternalResponse => "invalid_external_response",
                    _ => throw new ArgumentOutOfRangeException(nameof(error)),
                },
                message = error.Message,
                retryable = error.Retryable,
            },
        });
    }
}
