using System.Text.Json.Nodes;
using System.Text.Json;
using ItmoBot.Mcp.Infrastructure.Weather;
using ItmoBot.Mcp.Validation;
using ItmoBot.Mcp.Weather.Contracts;
using ItmoBot.Mcp.Weather;

namespace ItmoBot.Tests.Support;

internal sealed class WeatherFixtures
{
    public string Locations()
    {
        return """
            {"results":[{"name":"Тбилиси","country":"Грузия","country_code":"GE","admin1":"Тбилиси","latitude":41.7151,"longitude":44.8271,"timezone":"Asia/Tbilisi"}]}
            """;
    }

    public string Current()
    {
        return """
            {"timezone":"Asia/Tbilisi","current_units":{"time":"unixtime","temperature_2m":"°C","weather_code":"wmo code","wind_speed_10m":"km/h"},"current":{"time":1791784800,"temperature_2m":18.5,"weather_code":3,"wind_speed_10m":7.2}}
            """;
    }

    public JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public JsonObject LocationNode()
    {
        return JsonNode.Parse(Locations())!["results"]![0]!.DeepClone().AsObject();
    }

    public WeatherService Service(IOpenMeteoClient client, TimeProvider? clock = null, int deadline = 15)
    {
        var reader = new OpenMeteoJsonReader();
        var normalizer = new LocationTextNormalizer();
        return new WeatherService(client, new LocationQueryParser(normalizer), new OpenMeteoLocationParser(reader),
            new LocationSelector(normalizer), new OpenMeteoWeatherParser(reader), WeatherChecks.WeatherRequestTimeout(deadline), clock ?? TimeProvider.System);
    }
}
