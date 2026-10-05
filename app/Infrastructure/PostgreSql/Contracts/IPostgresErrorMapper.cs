using ItmoBot.Shared.Errors;

namespace ItmoBot.Infrastructure.PostgreSql.Contracts;

public interface IPostgresErrorMapper
{
    AppError Map(Exception error);
}
