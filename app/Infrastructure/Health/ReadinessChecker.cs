using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using System.Net;
using System.Text.Json;
using ItmoBot.Infrastructure.Health.ResultTypes;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.ValueObjects;

namespace ItmoBot.Infrastructure.Health;

public sealed class ReadinessChecker : IReadinessChecker
{
    private readonly HttpClient http;

    public ReadinessChecker(HttpClient? httpClient = null)
    {
        http = httpClient ?? new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public async Task<ReadinessCheckResult> CheckAsync(PortNumber port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{port.Value}/health", cancellationToken);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                return new ReadinessCheckResult.Failure(new AppError(
                    ErrorCode.ApplicationNotReady, "Приложение не готово. Проверьте подключение БД и работу polling.", IsRetryable: true));
            }

            if (!response.IsSuccessStatusCode)
            {
                return new ReadinessCheckResult.Failure(new AppError(
                    ErrorCode.HealthEndpointUnavailable, "Служебный HTTP endpoint вернул ошибку."));
            }

            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            if (body?.GetValueOrDefault("status") != "ok"
                || body.GetValueOrDefault("database") != "ok"
                || body.GetValueOrDefault("polling") != "running")
            {
                return new ReadinessCheckResult.Failure(new AppError(
                    ErrorCode.HealthResponseInvalid, "Служебный endpoint вернул некорректный ответ."));
            }

            return new ReadinessCheckResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return new ReadinessCheckResult.Failure(new AppError(
                ErrorCode.HealthResponseInvalid, "Служебный endpoint вернул некорректный JSON."));
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            return new ReadinessCheckResult.Failure(new AppError(
                ErrorCode.HealthEndpointUnavailable, "Не удалось проверить приложение. Убедитесь, что бот запущен и HEALTH_PORT указан правильно.", IsRetryable: true));
        }
    }

    public void Dispose()
    {
        http.Dispose();
    }
}
