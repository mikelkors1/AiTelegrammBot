using ItmoBot.Application.Health;
using ItmoBot.Application.Messaging;
using ItmoBot.Application.Storage;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;

namespace ItmoBot.Application;

public sealed class BotApplication(
    IDatabase database,
    ITelegramGateway telegram,
    ILogger logger,
    IReadinessServer readiness,
    IMessageHandler handler)
    : IBotApplication
{
    private const int MaximumConcurrentUpdates = 8;
    private const int MaximumConcurrentCallbacks = 2;

    public async Task<Result> RunAsync(CancellationToken cancellationToken)
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
            return new Result.Failure(new AppError(ErrorCode.UnexpectedFailure, "Не удалось запустить бот. Проверьте БД, токен и прокси."));
        }
    }

    private async Task<Result> RunCoreAsync(CancellationToken cancellationToken)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<Result>? pollingTask = null;
        try
        {
            var databaseCheck = await database.CheckAsync(stopping.Token);

            if (databaseCheck is Result.Failure databaseFailure)
            {
                return new Result.Failure(databaseFailure.Error);
            }

            logger.LogInformation("PostgreSQL подключён: SELECT 1 выполнен.");
            Result<string> initialization = await telegram.InitializeAsync(stopping.Token);

            if (initialization is Result<string>.Failure failure)
            {
                return new Result.Failure(failure.Error);
            }

            Result<string>.Success success = (Result<string>.Success)initialization;
            logger.LogInformation("Telegram доступен. Бот @{Username} запускает polling.", success.Value);
            var readinessStart = await readiness.StartAsync(stopping.Token);

            if (readinessStart is Result.Failure readinessFailure)
            {
                return new Result.Failure(readinessFailure.Error);
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

    private async Task<Result> PollAsync(CancellationToken cancellationToken)
    {
        using var processing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int offset = AgentChecks.UpdateOffset(0);
        var pending = new Dictionary<int, Task<Result<bool>>>();
        var completed = new HashSet<int>();
        var chatByUpdate = new Dictionary<int, long>();
        var lastByChat = new Dictionary<long, Task<Result<bool>>>();
        using var concurrency = new SemaphoreSlim(MaximumConcurrentUpdates, MaximumConcurrentUpdates);
        using var callbackConcurrency = new SemaphoreSlim(MaximumConcurrentCallbacks, MaximumConcurrentCallbacks);
        try
        {
            while (cancellationToken.IsCancellationRequested is false)
            {
                Result? completedResult = await ObserveCompletedUpdatesAsync(pending, completed, chatByUpdate,
                    lastByChat, cancellationToken, nextOffset => offset = nextOffset);
                if (completedResult is Result.Failure completedFailure)
                {
                    return completedFailure;
                }

                Result<IReadOnlyList<IncomingUpdate>> polling = await telegram.GetUpdatesAsync(offset, cancellationToken);
                if (polling is Result<IReadOnlyList<IncomingUpdate>>.Failure pollingFailure)
                {
                    if (!pollingFailure.Error.IsRetryable)
                    {
                        return new Result.Failure(pollingFailure.Error);
                    }

                    logger.LogWarning("{Code}: {Message}", pollingFailure.Error.Code, pollingFailure.Message);
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                    continue;
                }

                bool scheduled = false;
                foreach (var update in ((Result<IReadOnlyList<IncomingUpdate>>.Success)polling).Value)
                {
                    if (update.Id < offset || pending.ContainsKey(update.Id))
                    {
                        continue;
                    }

                    bool isCallback = update.Callback is not null;
                    long chatKey = update.ChatId ?? long.MinValue + update.Id;
                    Task<Result<bool>>? previous = null;
                    if (!isCallback)
                    {
                        lastByChat.TryGetValue(chatKey, out previous);
                    }

                    var updateConcurrency = isCallback ? callbackConcurrency : concurrency;
                    var task = HandleUpdateAsync(update, previous, updateConcurrency, processing.Token);
                    pending.Add(update.Id, task);
                    chatByUpdate.Add(update.Id, chatKey);
                    if (!isCallback)
                    {
                        lastByChat[chatKey] = task;
                    }
                    scheduled = true;
                }

                if (!scheduled && pending.Count > 0)
                {
                    Task[] active = pending.Values.Where(task => !task.IsCompleted).Cast<Task>().ToArray();
                    if (active.Length > 0)
                    {
                        await Task.WhenAny(Task.WhenAny(active), Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ожидаемая остановка polling.
        }
        finally
        {
            processing.Cancel();
            try
            {
                await Task.WhenAll(pending.Values);
            }
            catch (OperationCanceledException) when (processing.IsCancellationRequested)
            {
                // Обработчики обновлений отменены вместе с polling.
            }
            catch (Exception)
            {
                // Ошибки обработчиков будут записаны при обычной обработке; при остановке они не меняют результат.
            }
        }

        return new Result.Success();
    }

    private async Task<Result<bool>> HandleUpdateAsync(IncomingUpdate update, Task<Result<bool>>? previous,
        SemaphoreSlim concurrency, CancellationToken cancellationToken)
    {
        if (previous is not null)
        {
            try
            {
                await previous;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Ошибка предыдущего обновления не блокирует последующие сообщения этого чата.
            }
        }

        await concurrency.WaitAsync(cancellationToken);
        try
        {
            return await handler.HandleAsync(update, cancellationToken);
        }
        finally
        {
            concurrency.Release();
        }
    }

    private async Task<Result?> ObserveCompletedUpdatesAsync(Dictionary<int, Task<Result<bool>>> pending,
        HashSet<int> completed, Dictionary<int, long> chatByUpdate, Dictionary<long, Task<Result<bool>>> lastByChat,
        CancellationToken cancellationToken, Action<int> advanceOffset)
    {
        foreach (var entry in pending)
        {
            if (entry.Value.IsCompleted is false || completed.Contains(entry.Key))
            {
                continue;
            }

            Result<bool> handling;
            try
            {
                handling = await entry.Value;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                logger.LogError("Непредвиденная ошибка обработчика обновления Telegram: {Code}.", ErrorCode.UnexpectedFailure);
                return new Result.Failure(new AppError(ErrorCode.UnexpectedFailure,
                    "Не удалось обработать обновление Telegram."));
            }

            if (handling is Result<bool>.Failure handlingFailure)
            {
                if (handlingFailure.Error.Code is ErrorCode.TelegramUnauthorized or ErrorCode.TelegramPollingConflict)
                {
                    return new Result.Failure(handlingFailure.Error);
                }

                logger.LogWarning("{Code}: {Message}", handlingFailure.Error.Code, handlingFailure.Message);
            }

            completed.Add(entry.Key);
        }

        foreach (int updateId in pending.Keys.Order().ToArray())
        {
            if (!completed.Remove(updateId))
            {
                break;
            }

            var task = pending[updateId];
            long chatKey = chatByUpdate[updateId];
            if (lastByChat.TryGetValue(chatKey, out var last) && ReferenceEquals(last, task))
            {
                lastByChat.Remove(chatKey);
            }

            pending.Remove(updateId);
            chatByUpdate.Remove(updateId);
            advanceOffset(AgentChecks.UpdateOffset(updateId + 1));
        }

        return null;
    }
}
