namespace ItmoBot.Shared.Errors;

public sealed record class AppError(ErrorCode Code, string Message, bool IsRetryable = false);
