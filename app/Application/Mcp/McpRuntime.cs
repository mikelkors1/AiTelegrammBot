using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Application.Mcp;

public sealed class McpRuntime(IMcpClient client, ILogger logger) : IMcpRuntime
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IMcpSession? session;
    private Result<McpCatalog> catalog = new Result<McpCatalog>.Failure(new AppError(
        ErrorCode.McpUnavailable, "Подключение к MCP-серверу ещё не подготовлено."));
    private bool initialized;
    private bool disposed;

    public async Task<Result<McpCatalog>> InitializeAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (initialized)
            {
                return catalog;
            }

            return await InitializeCoreAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result<McpCatalog>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (session is null)
            {
                return catalog;
            }

            return await CheckCatalogAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result<McpToolOutput>> CallReadToolAsync(LlmToolCall call, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (session is null || catalog is not Result<McpCatalog>.Success available
                || !available.Value.Tools.Any(tool => tool.Name == call.Name))
            {
                return new Result<McpToolOutput>.Failure(new AppError(ErrorCode.McpUnavailable, "Инструмент временно недоступен."));
            }

            return await session.CallReadToolAsync(call, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AgentStorageResult<ReminderCreation>> ExecuteReminderAsync(PreparedAction action, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (session is null || catalog is not Result<McpCatalog>.Success available
                || !available.Value.Tools.Any(tool => tool.Name == "add_reminder"))
            {
                return new AgentStorageResult<ReminderCreation>.Failure(new AppError(ErrorCode.McpUnavailable, "Создание напоминаний временно недоступно."));
            }

            return await session.ExecuteReminderAsync(action, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            catalog = new Result<McpCatalog>.Failure(new AppError(ErrorCode.McpUnavailable, "MCP-соединение закрыто."));
            await CloseSessionAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Result<McpCatalog>> InitializeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            Result<IMcpSession> connected = await client.ConnectAsync(cancellationToken);
            if (connected is Result<IMcpSession>.Failure connectionFailure)
            {
                initialized = true;
                return await MarkUnavailableAsync(connectionFailure.Error);
            }

            session = ((Result<IMcpSession>.Success)connected).Value;
            var discovered = await session.DiscoverAsync(cancellationToken);
            initialized = true;
            if (discovered is Result<McpCatalog>.Failure discoveryFailure)
            {
                return await MarkUnavailableAsync(discoveryFailure.Error);
            }

            catalog = discovered;
            var available = ((Result<McpCatalog>.Success)catalog).Value;
            logger.LogInformation("MCP подключён: обнаружено инструментов {Tools}, ресурсов {Resources}.",
                available.Tools.Count, available.Resources.Count);
            return catalog;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CloseSessionAsync();
            throw;
        }
        catch (Exception)
        {
            initialized = true;
            return await MarkUnavailableAsync(new AppError(ErrorCode.McpUnavailable,
                "Не удалось подготовить MCP-инструменты. Обычный диалог остаётся доступен.", IsRetryable: true));
        }
    }

    private async Task<Result<McpCatalog>> CheckCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            var checkedConnection = await session!.CheckConnectionAsync(cancellationToken);
            if (checkedConnection is Result.Failure failure)
            {
                return await MarkUnavailableAsync(failure.Error);
            }

            return catalog;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return await MarkUnavailableAsync(new AppError(ErrorCode.McpUnavailable,
                "Связь с MCP-сервером потеряна. Инструменты временно недоступны.", IsRetryable: true));
        }
    }

    private async Task<Result<McpCatalog>> MarkUnavailableAsync(AppError error)
    {
        catalog = new Result<McpCatalog>.Failure(error);
        logger.LogWarning("MCP-инструменты недоступны; обычный диалог продолжает работать: {Code}.", error.Code);
        await CloseSessionAsync();
        return catalog;
    }

    private async Task CloseSessionAsync()
    {
        var previous = session;
        session = null;
        if (previous is null)
        {
            return;
        }

        try
        {
            await previous.DisposeAsync();
        }
        catch (Exception)
        {
            logger.LogWarning("Ошибка освобождения MCP-сессии: {Code}.", ErrorCode.McpUnavailable);
        }
    }
}
