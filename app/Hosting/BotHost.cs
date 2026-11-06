using ItmoBot.Application.Mcp;
using ItmoBot.Application.Storage;
using ItmoBot.Application;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;

namespace ItmoBot.Hosting;

public sealed class BotHost(IServiceScopeFactory scopeFactory, ILogger logger)
{
    public async Task<Result<int>> MigrateAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>().ApplyAsync(cancellationToken);
    }

    public async Task<Result> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var application = services.GetRequiredService<IBotApplication>();
            var migrated = await services.GetRequiredService<IDatabaseMigrator>().ApplyAsync(cancellationToken);
            if (migrated is Result<int>.Failure failure)
            {
                return new Result.Failure(failure.Error);
            }

            await services.GetRequiredService<IMcpRuntime>().InitializeAsync(cancellationToken);
            return await application.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Не удалось создать или освободить ресурсы приложения.");
            return new Result.Failure(new AppError(
                ErrorCode.UnexpectedFailure, "Не удалось подготовить ресурсы приложения. Проверьте конфигурацию и логи."));
        }
    }
}
