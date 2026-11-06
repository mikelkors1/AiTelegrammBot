using ItmoBot.Application.Messaging;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Validation;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class FakeTelegram : ITelegramGateway
{
    public bool Disposed
    {
        get; private set;
    }
    public List<(long, string)> Sent { get; } = [];
    public List<TelegramTextFormat> SentFormats { get; } = [];
    public List<TelegramReplyKeyboard?> SentKeyboards { get; } = [];
    public List<(long ChatId, int MessageId)> Deleted { get; } = [];
    public List<(long ChatId, byte[] Photo)> SentPhotos { get; } = [];
    public List<string?> PhotoCaptions { get; } = [];
    public AppError? PhotoError
    {
        get; set;
    }
    public AppError? DeleteError
    {
        get; set;
    }
    public AppError? EditError
    {
        get; set;
    }
    public bool InitializationFails
    {
        get; init;
    }
    public AppError? SendError
    {
        get; init;
    }
    public Func<long, string, int, AppError?>? SendFailure
    {
        get; set;
    }
    public int SendCalls
    {
        get; private set;
    }
    public AppError? PollError
    {
        get; init;
    }
    public IReadOnlyList<IncomingUpdate>? InitialUpdates
    {
        get; init;
    }
    public Func<int, CancellationToken, Task<Result<IReadOnlyList<IncomingUpdate>>>>? OnPoll
    {
        get; init;
    }

    public int InitializeCalls
    {
        get; private set;
    }
    public int PollCalls
    {
        get; private set;
    }
    public bool PollCancelled
    {
        get; private set;
    }
    public TaskCompletionSource PollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondPollStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<int> PollOffsets { get; } = [];

    public Task<Result<string>> InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitializeCalls++;
        if (InitializationFails)
        {
            return Task.FromResult<Result<string>>(new Result<string>.Failure(new AppError(
                ErrorCode.TelegramUnavailable, "Telegram недоступен", IsRetryable: true)));
        }

        return Task.FromResult<Result<string>>(new Result<string>.Success(TelegramChecks.BotUsername("test_bot")));
    }

    public async Task<Result<IReadOnlyList<IncomingUpdate>>> GetUpdatesAsync(int offset, CancellationToken cancellationToken)
    {
        PollCalls++;
        PollOffsets.Add(offset);
        PollStarted.TrySetResult();
        if (PollCalls == 2)
        {
            SecondPollStarted.TrySetResult();
        }
        if (OnPoll is not null)
        {
            return await OnPoll(offset, cancellationToken);
        }

        if (PollError is not null)
        {
            return new Result<IReadOnlyList<IncomingUpdate>>.Failure(PollError);
        }

        if (PollCalls == 1 && InitialUpdates is not null)
        {
            return new Result<IReadOnlyList<IncomingUpdate>>.Success(InitialUpdates.ToArray());
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            PollCancelled = true;
            throw;
        }

        return new Result<IReadOnlyList<IncomingUpdate>>.Success([]);
    }

    public Task<Result<int>> SendTextAsync(long chatId, string text, CancellationToken cancellationToken,
        TelegramTextFormat format = TelegramTextFormat.Plain, TelegramReplyKeyboard? keyboard = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendCalls++;
        var error = SendFailure?.Invoke(chatId, text, SendCalls) ?? SendError;
        if (error is not null)
        {
            return Task.FromResult<Result<int>>(new Result<int>.Failure(error));
        }

        Sent.Add((chatId, text));
        SentFormats.Add(format);
        SentKeyboards.Add(keyboard);
        return Task.FromResult<Result<int>>(new Result<int>.Success(TelegramChecks.MessageId(SendCalls)));
    }

    public Task<Result> DeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Deleted.Add((chatId, messageId));
        return Task.FromResult<Result>(DeleteError is null
            ? new Result.Success()
            : new Result.Failure(DeleteError));
    }

    public async Task<Result<int>> SendPhotoAsync(long chatId, Stream photo, CancellationToken cancellationToken,
        string? caption = null, TelegramReplyKeyboard? keyboard = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PhotoError is not null)
        {
            return new Result<int>.Failure(PhotoError);
        }

        using var content = new MemoryStream();
        await photo.CopyToAsync(content, cancellationToken);
        SentPhotos.Add((chatId, content.ToArray()));
        PhotoCaptions.Add(caption);
        SentKeyboards.Add(keyboard);
        return new Result<int>.Success(TelegramChecks.MessageId(1000 + SentPhotos.Count));
    }

    public void Dispose()
    {
        Disposed = true;
    }
    public readonly List<TelegramInlineKeyboard> InlineKeyboards = [];
    public readonly List<string> AnsweredCallbacks = [];
    public readonly List<int> ClearedKeyboards = [];

    public Task<Result<int>> SendInlineTextAsync(long chatId, string text, TelegramInlineKeyboard keyboard, CancellationToken cancellationToken)
    {
        InlineKeyboards.Add(keyboard);
        return SendTextAsync(chatId, text, cancellationToken);
    }

    public Task<Result> AnswerCallbackAsync(string callbackId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AnsweredCallbacks.Add(callbackId);
        return Task.FromResult<Result>(new Result.Success());
    }

    public Task<Result> RemoveInlineKeyboardAsync(long chatId, int messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClearedKeyboards.Add(messageId);
        return Task.FromResult<Result>(EditError is null ? new Result.Success() : new Result.Failure(EditError));
    }

}
