using ItmoBot.Application.ResultTypes;
using ItmoBot.Hosting.Contracts;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Hosting;

public sealed class BotHost(IBotSessionFactory sessionFactory, ILogger logger) : IBotHost
{
    public async Task<BotExecutionResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var session = await sessionFactory.CreateAsync();
            var initialized = await session.InitializeAsync(cancellationToken);
            if (initialized is DatabaseMigrationResult.Failure failure)
            {
                return new BotExecutionResult.Failure(failure.Error);
            }

            return await session.Application.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Не удалось создать или освободить ресурсы приложения.");
            return new BotExecutionResult.Failure(new AppError(
                ErrorCode.UnexpectedFailure, "Не удалось подготовить ресурсы приложения. Проверьте конфигурацию и логи."));
        }
    }
}
