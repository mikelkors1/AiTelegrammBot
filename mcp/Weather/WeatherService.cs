using ItmoBot.Mcp.Infrastructure.Weather;
using ItmoBot.Mcp.Weather.Contracts;
using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;

namespace ItmoBot.Mcp.Weather;

public sealed class WeatherService(IOpenMeteoClient client, LocationQueryParser queries, OpenMeteoLocationParser locations,
    LocationSelector selector, OpenMeteoWeatherParser readings, TimeSpan deadline, TimeProvider clock) : IWeatherService
{
    public async Task<WeatherResult> GetCurrentWeatherByCityAsync(string query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(deadline, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await GetWeatherCoreAsync(query, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return new WeatherResult.Failure(new WeatherError(WeatherErrorCode.ExternalTimeout,
                "Не удалось получить погоду за отведённое время.", Retryable: true));
        }
    }

    private async Task<WeatherResult> GetWeatherCoreAsync(string query, CancellationToken cancellationToken)
    {
        var resolution = await ResolveLocationAsync(query, cancellationToken);
        switch (resolution)
        {
            case LocationResolutionResult.NotFound:
                return new WeatherResult.NotFound(query);
            case LocationResolutionResult.Ambiguous ambiguous:
                return new WeatherResult.Ambiguous(ambiguous.Candidates);
            case LocationResolutionResult.Failure failure:
                return new WeatherResult.Failure(failure.Error);
        }

        var location = ((LocationResolutionResult.Success)resolution).Location;
        var response = await client.GetCurrentWeatherByCoordinatesAsync(location.Coordinates, cancellationToken);
        if (response is WeatherHttpResult.Failure httpFailure)
        {
            return new WeatherResult.Failure(httpFailure.Error);
        }

        var parsed = readings.ParseCurrentWeather(((WeatherHttpResult.Success)response).Value, location);
        return parsed is WeatherDataResult<CurrentWeather>.Failure parseFailure
            ? new WeatherResult.Failure(parseFailure.Error)
            : new WeatherResult.Success(((WeatherDataResult<CurrentWeather>.Success)parsed).Value);
    }

    private async Task<LocationResolutionResult> ResolveLocationAsync(string query, CancellationToken cancellationToken)
    {
        var parsedQuery = queries.ParseCityQuery(query);
        if (parsedQuery is WeatherDataResult<LocationSearchQuery>.Failure queryFailure)
        {
            return new LocationResolutionResult.Failure(queryFailure.Error);
        }

        var search = ((WeatherDataResult<LocationSearchQuery>.Success)parsedQuery).Value;
        var response = await client.SearchLocationsByNameAsync(search.SearchText, cancellationToken, search.CountryFilter);
        if (response is WeatherHttpResult.Failure httpFailure)
        {
            return new LocationResolutionResult.Failure(httpFailure.Error);
        }

        var parsed = locations.ParseLocations(((WeatherHttpResult.Success)response).Value);
        return parsed is WeatherDataResult<IReadOnlyList<WeatherLocation>>.Failure parseFailure
            ? new LocationResolutionResult.Failure(parseFailure.Error)
            : selector.SelectLocation(search, ((WeatherDataResult<IReadOnlyList<WeatherLocation>>.Success)parsed).Value);
    }
}
