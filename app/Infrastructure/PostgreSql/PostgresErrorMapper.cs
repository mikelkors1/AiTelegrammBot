using ItmoBot.Infrastructure.PostgreSql.Contracts;
using ItmoBot.Shared.Errors;
using Npgsql;

namespace ItmoBot.Infrastructure.PostgreSql;

public sealed class PostgresErrorMapper : IPostgresErrorMapper
{
    public AppError Map(Exception error)
    {
        return error switch
        {
            PostgresException { SqlState: "28P01" or "28000" } => new AppError(ErrorCode.DatabaseAuthenticationFailed,
                "PostgreSQL отклонил авторизацию. Проверьте пользователя и пароль."),
            NpgsqlException { InnerException: TimeoutException } or OperationCanceledException or TimeoutException => new AppError(ErrorCode.DatabaseTimeout,
                "Истекло время ожидания PostgreSQL. Попробуйте позже.", IsRetryable: true),
            PostgresException { SqlState: "40001" or "40P01" } => new AppError(ErrorCode.DatabaseOperationFailed,
                "Не удалось завершить изменение диалога из-за конкурирующей операции. Попробуйте снова.", IsRetryable: true),
            PostgresException => new AppError(ErrorCode.DatabaseOperationFailed,
                "Не удалось выполнить операцию с диалогом. Проверьте миграции и права доступа к БД."),
            NpgsqlException or IOException => new AppError(ErrorCode.DatabaseUnavailable,
                "PostgreSQL недоступен. Проверьте подключение к БД.", IsRetryable: true),
            _ => new AppError(ErrorCode.InvalidConversationState,
                "Хранилище вернуло некорректные данные диалога. Проверьте состояние БД."),
        };
    }
}
