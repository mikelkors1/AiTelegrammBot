using System.Text.Json;
using ItmoBot.Mcp.Weather.Contracts;
using ItmoBot.Mcp.Weather.Models;
using ItmoBot.Mcp.Weather.ResultTypes;

namespace ItmoBot.Mcp.Infrastructure.Weather;

public sealed class OpenMeteoClient(HttpClient client, OpenMeteoRequestFactory requests,
    WeatherHttpSettings settings, TimeProvider clock) : IOpenMeteoClient, IDisposable
{
    private bool disposed;

    public Task<WeatherHttpResult> SearchLocationsByNameAsync(string query, CancellationToken cancellationToken, string? countryFilter = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        return SendAsync(() => requests.CreateLocationSearchRequest(query, countryFilter), cancellationToken);
    }

    public Task<WeatherHttpResult> GetCurrentWeatherByCoordinatesAsync(GeoCoordinates coordinates, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        return SendAsync(() => requests.CreateCurrentWeatherRequest(coordinates), cancellationToken);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        client.Dispose();
    }

    private async Task<WeatherHttpResult> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        for (var attempt = 1; attempt <= settings.Attempts; attempt++)
        {
            var response = await SendOnceAsync(createRequest, cancellationToken);
            if (response.RetryDelay is not { } delay || attempt == settings.Attempts)
            {
                return response.Result;
            }

            await Task.Delay(delay, clock, cancellationToken);
        }

        throw new InvalidOperationException("Некорректное число попыток HTTP-запроса.");
    }

    private async Task<WeatherHttpAttempt> SendOnceAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(settings.Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var request = createRequest();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                var transient = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode is >= 500 and <= 599;
                return new WeatherHttpAttempt(Unavailable(transient), transient ? RetryDelay(response) : null);
            }

            return new WeatherHttpAttempt(await ReadJsonAsync(response, linked.Token));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new WeatherHttpAttempt(new WeatherHttpResult.Failure(new WeatherError(
                WeatherErrorCode.ExternalTimeout, "Погодный сервис не ответил за отведённое время.", Retryable: true)));
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            return new WeatherHttpAttempt(Unavailable(true), TimeSpan.FromMilliseconds(250));
        }
        catch (JsonException)
        {
            return new WeatherHttpAttempt(InvalidResponse());
        }
    }

    private async Task<WeatherHttpResult> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int maximumBytes = 1_048_576;
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            return InvalidResponse();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > maximumBytes)
            {
                return InvalidResponse();
            }

            buffer.Write(chunk, 0, count);
        }

        buffer.Position = 0;
        using var document = await JsonDocument.ParseAsync(buffer, new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.False))
        {
            return InvalidResponse();
        }

        return new WeatherHttpResult.Success(root.Clone());
    }

    private TimeSpan? RetryDelay(HttpResponseMessage response)
    {
        var delay = response.Headers.RetryAfter?.Delta
            ?? response.Headers.RetryAfter?.Date - clock.GetUtcNow()
            ?? TimeSpan.FromMilliseconds(250);
        if (delay > TimeSpan.FromSeconds(1))
        {
            return null;
        }

        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private WeatherHttpResult.Failure Unavailable(bool retryable)
    {
        return new WeatherHttpResult.Failure(new WeatherError(WeatherErrorCode.SourceUnavailable,
            "Не удалось получить данные погодного сервиса.", retryable));
    }

    private WeatherHttpResult.Failure InvalidResponse()
    {
        return new WeatherHttpResult.Failure(new WeatherError(WeatherErrorCode.InvalidExternalResponse,
            "Погодный сервис вернул некорректный ответ.", Retryable: false));
    }
}
