using ItmoBot.Application.Models;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Shared.Errors;
using ChatId = ItmoBot.Application.ValueObjects.ChatId;

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
    public List<(ChatId ChatId, MessageId MessageId)> Deleted { get; } = [];
    public List<(ChatId ChatId, byte[] Photo)> SentPhotos { get; } = [];
    public List<TelegramCaption?> PhotoCaptions { get; } = [];
    public AppError? PhotoError
    {
        get; set;
    }
    public AppError? DeleteError
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
    public Func<ChatId, TelegramText, int, AppError?>? SendFailure
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

    public Task<TelegramInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitializeCalls++;
        if (InitializationFails)
        {
            return Task.FromResult<TelegramInitializationResult>(new TelegramInitializationResult.Failure(new AppError(
                ErrorCode.TelegramUnavailable, "Telegram недоступен", IsRetryable: true)));
        }

        return Task.FromResult<TelegramInitializationResult>(new TelegramInitializationResult.Success(new BotUsername("test_bot")));
    }

    public async Task<TelegramPollingResult> GetUpdatesAsync(UpdateOffset offset, CancellationToken cancellationToken)
    {
        PollCalls++;
        PollStarted.TrySetResult();
        if (PollError is not null)
        {
            return new TelegramPollingResult.Failure(PollError);
        }

        if (PollCalls == 1 && InitialUpdates is not null)
        {
            return new TelegramPollingResult.Success(InitialUpdates.ToArray());
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

        return new TelegramPollingResult.Success([]);
    }

    public Task<TelegramSendResult> SendTextAsync(ChatId chatId, TelegramText text, CancellationToken cancellationToken,
        TelegramTextFormat format = TelegramTextFormat.Plain, TelegramReplyKeyboard? keyboard = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SendCalls++;
        var error = SendFailure?.Invoke(chatId, text, SendCalls) ?? SendError;
        if (error is not null)
        {
            return Task.FromResult<TelegramSendResult>(new TelegramSendResult.Failure(error));
        }

        Sent.Add((chatId.Value, text.Value));
        SentFormats.Add(format);
        SentKeyboards.Add(keyboard);
        return Task.FromResult<TelegramSendResult>(new TelegramSendResult.Success(new MessageId(SendCalls)));
    }

    public Task<TelegramDeleteResult> DeleteMessageAsync(ChatId chatId, MessageId messageId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Deleted.Add((chatId, messageId));
        return Task.FromResult<TelegramDeleteResult>(DeleteError is null
            ? new TelegramDeleteResult.Success()
            : new TelegramDeleteResult.Failure(DeleteError));
    }

    public async Task<TelegramSendResult> SendPhotoAsync(ChatId chatId, Stream photo, CancellationToken cancellationToken,
        TelegramCaption? caption = null, TelegramReplyKeyboard? keyboard = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PhotoError is not null)
        {
            return new TelegramSendResult.Failure(PhotoError);
        }

        using var content = new MemoryStream();
        await photo.CopyToAsync(content, cancellationToken);
        SentPhotos.Add((chatId, content.ToArray()));
        PhotoCaptions.Add(caption);
        SentKeyboards.Add(keyboard);
        return new TelegramSendResult.Success(new MessageId(1000 + SentPhotos.Count));
    }

    public void Dispose()
    {
        Disposed = true;
    }
}
