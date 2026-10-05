using ItmoBot.Application.Contracts;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;
using ItmoBot.Application.ResultTypes;

namespace ItmoBot.Application;

public sealed class BotApplication(IDatabase database, ITelegramGateway telegram, ILogger logger, IReadinessServer readiness, IMessageHandler handler) : IBotApplication
{
    public async Task<BotExecutionResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await RunCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Не удалось запустить бот. Проверьте БД, токен и прокси.");
            return new BotExecutionResult.Failure(new AppError(ErrorCode.UnexpectedFailure, "Не удалось запустить бот. Проверьте БД, токен и прокси."));
        }
    }

    private async Task<BotExecutionResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<BotExecutionResult>? pollingTask = null;
        try
        {
            var databaseCheck = await database.CheckAsync(stopping.Token);

            if (databaseCheck is DatabaseCheckResult.Failure databaseFailure)
            {
                return new BotExecutionResult.Failure(databaseFailure.Error);
            }

            logger.LogInformation("PostgreSQL подключён: SELECT 1 выполнен.");
            var initialization = await telegram.InitializeAsync(stopping.Token);

            if (initialization is TelegramInitializationResult.Failure failure)
            {
                return new BotExecutionResult.Failure(failure.Error);
            }

            var success = (TelegramInitializationResult.Success)initialization;
            logger.LogInformation("Telegram доступен. Бот @{Username} запускает polling.", success.Username);
            var readinessStart = await readiness.StartAsync(stopping.Token);

            if (readinessStart is ReadinessServerStartResult.Failure readinessFailure)
            {
                return new BotExecutionResult.Failure(readinessFailure.Error);
            }

            using var serverStopped = readiness.Stopping.Register(stopping.Cancel);
            pollingTask = PollAsync(stopping.Token);
            readiness.MarkPolling(pollingTask);
            return await pollingTask;
        }
        finally
        {
            readiness.MarkStopped();
            stopping.Cancel();
            try
            {
                if (pollingTask is not null)
                {
                    try
                    {
                        await pollingTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Отмена при завершении приложения ожидаема.
                    }
                }
            }
            finally
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await readiness.StopAsync(timeout.Token);
            }
        }
    }

    private async Task<BotExecutionResult> PollAsync(CancellationToken cancellationToken)
    {
        var offset = UpdateOffset.Zero;
        while (cancellationToken.IsCancellationRequested is false)
        {
            try
            {
                var polling = await telegram.GetUpdatesAsync(offset, cancellationToken);
                if (polling is TelegramPollingResult.Failure pollingFailure)
                {
                    if (!pollingFailure.Error.IsRetryable)
                    {
                        return new BotExecutionResult.Failure(pollingFailure.Error);
                    }

                    logger.LogWarning("{Code}: {Message}", pollingFailure.Error.Code, pollingFailure.Message);
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                    continue;
                }

                foreach (var update in ((TelegramPollingResult.Success)polling).Updates)
                {
                    var handling = await handler.HandleAsync(update, cancellationToken);
                    if (handling is MessageHandlingResult.Failure handlingFailure)
                    {
                        if (handlingFailure.Error.Code is ErrorCode.TelegramUnauthorized or ErrorCode.TelegramPollingConflict)
                        {
                            return new BotExecutionResult.Failure(handlingFailure.Error);
                        }

                        logger.LogWarning("{Code}: {Message}", handlingFailure.Error.Code, handlingFailure.Message);
                    }

                    offset = update.Id.NextOffset();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return new BotExecutionResult.Success();
    }
}
