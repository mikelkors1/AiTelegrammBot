using ItmoBot.Shared.Errors;

namespace ItmoBot.Infrastructure.Llm.Contracts;

public interface ILlmErrorMapper
{
    AppError MapStatus(int status);
    AppError MapException(Exception error);
}
