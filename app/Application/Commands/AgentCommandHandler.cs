using ItmoBot.Application.Agent.Contracts;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.Agent.ResultTypes;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Mcp;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Validation;

namespace ItmoBot.Application.Commands;

public sealed class AgentCommandHandler(IMcpRuntime runtime, IAgentUserRepository users,
    IAgentAuditRepository audit, AgentCommandReplyFormatter formatter)
{
    public async Task<Result<string>> ExecuteAsync(AgentTurnContext? context, BotCommand command, CancellationToken cancellationToken)
    {
        if (command is BotCommand.ShowTools)
        {
            var catalog = await runtime.GetCatalogAsync(cancellationToken);
            return catalog is Result<McpCatalog>.Success success
                ? new Result<string>.Success(formatter.Tools(success.Value))
                : new Result<string>.Failure(((Result<McpCatalog>.Failure)catalog).Error);
        }

        if (context is null || context.Owner != context.ChatId)
        {
            return new Result<string>.Failure(new AppError(ErrorCode.Unauthorized, "Команда доступна только своему пользователю в личном чате."));
        }

        if (command is BotCommand.SetTimeZone set)
        {
            var saved = await users.SetTimeZoneAsync(context.Owner, set.TimeZone, cancellationToken);
            return saved is AgentStorageResult<UserTimeZone>.Success success
                ? new Result<string>.Success(formatter.TimeZone(success.Value))
                : new Result<string>.Failure(((AgentStorageResult<UserTimeZone>.Failure)saved).Error);
        }

        var timezone = await users.GetTimeZoneAsync(context.Owner, cancellationToken);
        if (timezone is AgentStorageResult<UserTimeZone?>.Failure failure)
        {
            return new Result<string>.Failure(failure.Error);
        }

        var zone = ((AgentStorageResult<UserTimeZone?>.Success)timezone).Value;
        if (command is BotCommand.ShowTimeZone)
        {
            return new Result<string>.Success(formatter.TimeZone(zone));
        }

        if (command is not BotCommand.ShowWhy)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        var latest = await audit.GetLatestEventAsync(context.Owner, context.ChatId, cancellationToken);
        return latest switch
        {
            AgentStorageResult<AgentAuditEvent>.Success success => new Result<string>.Success(formatter.Why(success.Value, zone)),
            AgentStorageResult<AgentAuditEvent>.Failure error => new Result<string>.Failure(error.Error),
            _ => new Result<string>.Success(RequestChecks.LlmText("Для вас пока нет записей о решениях агента.")),
        };
    }
}
