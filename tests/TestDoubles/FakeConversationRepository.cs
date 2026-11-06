using ItmoBot.Application.Conversations;
using ItmoBot.Application.Llm;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Validation;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeConversationRepository : IConversationRepository
{
    public Dictionary<long, ConversationState> States { get; } = [];
    public Dictionary<Guid, TestStoredTurn> Turns { get; } = [];
    public AppError? LoadError
    {
        get; set;
    }
    public AppError? BeginError
    {
        get; set;
    }
    public AppError? CompleteError
    {
        get; set;
    }
    public int FailedCalls
    {
        get; private set;
    }
    public int BeginCalls
    {
        get; private set;
    }

    public Task<Result<ConversationSnapshot>> LoadAsync(long chatId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (LoadError is not null)
        {
            return Task.FromResult<Result<ConversationSnapshot>>(new Result<ConversationSnapshot>.Failure(LoadError));
        }

        var history = Turns.Values.Where(turn => turn.Turn.State.ChatId == chatId && turn.Status == ConversationTurnStatus.Succeeded)
            .SelectMany(turn => new[] { new LlmMessage(LlmMessageRole.User, turn.User), new LlmMessage(LlmMessageRole.Assistant, turn.Assistant!) }).ToArray();
        return Task.FromResult<Result<ConversationSnapshot>>(new Result<ConversationSnapshot>.Success(new ConversationSnapshot(State(chatId), Array.AsReadOnly(history))));
    }

    public Task<Result<ConversationState>> SetModeAsync(long chatId, AssistantMode mode, CancellationToken cancellationToken)
    {
        return ClearAsync(chatId, mode, cancellationToken);
    }

    public Task<Result<ConversationState>> SetTemperatureAsync(long chatId, decimal temperature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changed = State(chatId) with
        {
            Temperature = temperature
        };
        States[chatId] = changed;
        return Task.FromResult<Result<ConversationState>>(new Result<ConversationState>.Success(changed));
    }

    public Task<Result<ConversationState>> ResetAsync(long chatId, CancellationToken cancellationToken)
    {
        return ClearAsync(chatId, State(chatId).Mode, cancellationToken);
    }

    public Task<Result<ConversationTurn>> BeginTurnAsync(long chatId, string userText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeginCalls++;
        if (BeginError is not null)
        {
            return Task.FromResult<Result<ConversationTurn>>(new Result<ConversationTurn>.Failure(BeginError));
        }

        var turn = new ConversationTurn(RequestChecks.ConversationTurnId(Guid.NewGuid()), State(chatId));
        Turns.Add(turn.Id, new TestStoredTurn(turn, userText, null, ConversationTurnStatus.Pending));
        return Task.FromResult<Result<ConversationTurn>>(new Result<ConversationTurn>.Success(turn));
    }

    public Task<Result<ConversationTurnStatus>> CompleteTurnAsync(long chatId, Guid turnId, string assistantText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CompleteError is not null)
        {
            return Task.FromResult<Result<ConversationTurnStatus>>(new Result<ConversationTurnStatus>.Failure(CompleteError));
        }

        return FinishAsync(chatId, turnId, assistantText, cancellationToken);
    }

    public Task<Result<ConversationTurnStatus>> FailTurnAsync(long chatId, Guid turnId, CancellationToken cancellationToken)
    {
        FailedCalls++;
        return FinishAsync(chatId, turnId, null, cancellationToken);
    }

    private ConversationState State(long chatId)
    {
        if (!States.TryGetValue(chatId, out var state))
        {
            state = new ConversationState(chatId, AssistantMode.Study, RequestChecks.Temperature(0.3m), RequestChecks.ConversationRevision(0));
            States.Add(chatId, state);
        }

        return state;
    }

    private Task<Result<ConversationState>> ClearAsync(long chatId, AssistantMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = State(chatId);
        foreach (Guid id in Turns.Where(entry => entry.Value.Turn.State.ChatId == chatId).Select(entry => entry.Key).ToArray())
        {
            Turns.Remove(id);
        }

        var changed = state with
        {
            Mode = mode,
            Revision = RequestChecks.ConversationRevision(state.Revision + 1)
        };
        States[chatId] = changed;
        return Task.FromResult<Result<ConversationState>>(new Result<ConversationState>.Success(changed));
    }

    private Task<Result<ConversationTurnStatus>> FinishAsync(long chatId, Guid turnId, string? assistant, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Turns.TryGetValue(turnId, out var stored) || stored.Turn.State.ChatId != chatId)
        {
            return Task.FromResult<Result<ConversationTurnStatus>>(new Result<ConversationTurnStatus>.Failure(new AppError(ErrorCode.ConversationTurnNotFound, "Ход удалён.")));
        }

        var status = assistant is null ? ConversationTurnStatus.Failed : ConversationTurnStatus.Succeeded;
        Turns[turnId] = stored with
        {
            Assistant = assistant,
            Status = status
        };
        return Task.FromResult<Result<ConversationTurnStatus>>(new Result<ConversationTurnStatus>.Success(status));
    }
}
