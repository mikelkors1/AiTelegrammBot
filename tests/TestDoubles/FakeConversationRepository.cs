using ItmoBot.Application.Contracts;
using ItmoBot.Application.Models;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeConversationRepository : IConversationRepository
{
    public Dictionary<ChatId, ConversationState> States { get; } = [];
    public Dictionary<ConversationTurnId, TestStoredTurn> Turns { get; } = [];
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

    public Task<ConversationResult<ConversationSnapshot>> LoadAsync(ChatId chatId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (LoadError is not null)
        {
            return Task.FromResult<ConversationResult<ConversationSnapshot>>(new ConversationResult<ConversationSnapshot>.Failure(LoadError));
        }

        var history = Turns.Values.Where(turn => turn.Turn.State.ChatId == chatId && turn.Status == ConversationTurnStatus.Succeeded)
            .SelectMany(turn => new[] { new LlmMessage(LlmMessageRole.User, turn.User), new LlmMessage(LlmMessageRole.Assistant, turn.Assistant!) }).ToArray();
        return Task.FromResult<ConversationResult<ConversationSnapshot>>(new ConversationResult<ConversationSnapshot>.Success(new ConversationSnapshot(State(chatId), Array.AsReadOnly(history))));
    }

    public Task<ConversationResult<ConversationState>> SetModeAsync(ChatId chatId, AssistantMode mode, CancellationToken cancellationToken)
    {
        return ClearAsync(chatId, mode, cancellationToken);
    }

    public Task<ConversationResult<ConversationState>> SetTemperatureAsync(ChatId chatId, Temperature temperature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changed = State(chatId) with
        {
            Temperature = temperature
        };
        States[chatId] = changed;
        return Task.FromResult<ConversationResult<ConversationState>>(new ConversationResult<ConversationState>.Success(changed));
    }

    public Task<ConversationResult<ConversationState>> ResetAsync(ChatId chatId, CancellationToken cancellationToken)
    {
        return ClearAsync(chatId, State(chatId).Mode, cancellationToken);
    }

    public Task<ConversationResult<ConversationTurn>> BeginTurnAsync(ChatId chatId, LlmText userText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeginCalls++;
        if (BeginError is not null)
        {
            return Task.FromResult<ConversationResult<ConversationTurn>>(new ConversationResult<ConversationTurn>.Failure(BeginError));
        }

        var turn = new ConversationTurn(new ConversationTurnId(Guid.NewGuid()), State(chatId));
        Turns.Add(turn.Id, new TestStoredTurn(turn, userText, null, ConversationTurnStatus.Pending));
        return Task.FromResult<ConversationResult<ConversationTurn>>(new ConversationResult<ConversationTurn>.Success(turn));
    }

    public Task<ConversationResult<ConversationTurnStatus>> CompleteTurnAsync(ChatId chatId, ConversationTurnId turnId, LlmText assistantText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (CompleteError is not null)
        {
            return Task.FromResult<ConversationResult<ConversationTurnStatus>>(new ConversationResult<ConversationTurnStatus>.Failure(CompleteError));
        }

        return FinishAsync(chatId, turnId, assistantText, cancellationToken);
    }

    public Task<ConversationResult<ConversationTurnStatus>> FailTurnAsync(ChatId chatId, ConversationTurnId turnId, CancellationToken cancellationToken)
    {
        FailedCalls++;
        return FinishAsync(chatId, turnId, null, cancellationToken);
    }

    private ConversationState State(ChatId chatId)
    {
        if (!States.TryGetValue(chatId, out var state))
        {
            state = new ConversationState(chatId, AssistantMode.Study, new Temperature(0.3m), ConversationRevision.Zero);
            States.Add(chatId, state);
        }

        return state;
    }

    private Task<ConversationResult<ConversationState>> ClearAsync(ChatId chatId, AssistantMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = State(chatId);
        foreach (var id in Turns.Where(entry => entry.Value.Turn.State.ChatId == chatId).Select(entry => entry.Key).ToArray())
        {
            Turns.Remove(id);
        }

        var changed = state with
        {
            Mode = mode,
            Revision = new ConversationRevision(state.Revision.Value + 1)
        };
        States[chatId] = changed;
        return Task.FromResult<ConversationResult<ConversationState>>(new ConversationResult<ConversationState>.Success(changed));
    }

    private Task<ConversationResult<ConversationTurnStatus>> FinishAsync(ChatId chatId, ConversationTurnId turnId, LlmText? assistant, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Turns.TryGetValue(turnId, out var stored) || stored.Turn.State.ChatId != chatId)
        {
            return Task.FromResult<ConversationResult<ConversationTurnStatus>>(new ConversationResult<ConversationTurnStatus>.Failure(new AppError(ErrorCode.ConversationTurnNotFound, "Ход удалён.")));
        }

        var status = assistant is null ? ConversationTurnStatus.Failed : ConversationTurnStatus.Succeeded;
        Turns[turnId] = stored with
        {
            Assistant = assistant,
            Status = status
        };
        return Task.FromResult<ConversationResult<ConversationTurnStatus>>(new ConversationResult<ConversationTurnStatus>.Success(status));
    }
}
