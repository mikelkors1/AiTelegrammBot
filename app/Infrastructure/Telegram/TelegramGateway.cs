using ItmoBot.Application.Models;
using ItmoBot.Infrastructure.Telegram.Contracts;
using ChatId = ItmoBot.Application.ValueObjects.ChatId;
using MessageId = ItmoBot.Application.ValueObjects.MessageId;
using ItmoBot.Application.ResultTypes;
using ItmoBot.Application.Contracts;
using ItmoBot.Application.ValueObjects;
using ItmoBot.Configuration.Models;
using ItmoBot.Shared.Errors;
using System.Text.Json;
using ItmoBot.Shared.Exceptions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace ItmoBot.Infrastructure.Telegram;

public sealed class TelegramGateway : ITelegramGateway
{
    private readonly HttpClient http;
    private readonly ITelegramBotClient bot;
    private readonly ILogger? logger;
    private readonly ITelegramErrorMapper errorMapper;
    public TelegramGateway(Settings settings, HttpClient httpClient, ITelegramErrorMapper errorMapper, ILogger? logger = null)
    {
        this.logger = logger;
        http = httpClient;
        this.errorMapper = errorMapper;
        bot = new TelegramBotClient(new TelegramBotClientOptions(settings.BotToken.Value) { RetryThreshold = 0 }, http);
    }

    public async Task<TelegramInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var me = await bot.GetMe(timeout.Token);
            var webhook = await bot.GetWebhookInfo(timeout.Token);
            if (!string.IsNullOrEmpty(webhook.Url))
            {
                return new TelegramInitializationResult.Failure(new AppError(
                    ErrorCode.WebhookConfigured,
                    "У бота установлен webhook. Удалите его перед запуском polling или используйте отдельного учебного бота."));
            }

            return new TelegramInitializationResult.Success(new BotUsername(me.Username ?? ""));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RequestException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception error) when (IsExpected(error))
        {
            logger?.LogWarning(error, "Ошибка инициализации Telegram.");
            return new TelegramInitializationResult.Failure(errorMapper.Map(error));
        }
        catch (ValueObjectValidationException error)
        {
            return new TelegramInitializationResult.Failure(new AppError(error.Code, error.Message));
        }
    }

    public async Task<TelegramPollingResult> GetUpdatesAsync(UpdateOffset offset, CancellationToken cancellationToken)
    {
        try
        {
            var updates = await bot.GetUpdates(offset: offset.Value, timeout: 25,
                allowedUpdates: [UpdateType.Message], cancellationToken: cancellationToken);
            return new TelegramPollingResult.Success(updates.Select(update =>
                MapUpdate(update)).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RequestException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception error) when (IsExpected(error))
        {
            logger?.LogWarning(error, "Ошибка получения Telegram updates.");
            return new TelegramPollingResult.Failure(errorMapper.Map(error));
        }
        catch (ValueObjectValidationException error)
        {
            return new TelegramPollingResult.Failure(new AppError(error.Code, error.Message));
        }
    }

    public async Task<TelegramSendResult> SendTextAsync(ChatId chatId, TelegramText text, CancellationToken cancellationToken,
        TelegramTextFormat format = TelegramTextFormat.Plain, TelegramReplyKeyboard? keyboard = null)
    {
        try
        {
            var parseMode = format switch
            {
                TelegramTextFormat.Plain => ParseMode.None,
                TelegramTextFormat.Html => ParseMode.Html,
                _ => throw new ArgumentOutOfRangeException(nameof(format)),
            };
            var markup = CreateKeyboard(keyboard);
            var message = await bot.SendMessage(chatId.Value, text.Value, parseMode: parseMode,
                replyMarkup: markup, cancellationToken: cancellationToken);
            return new TelegramSendResult.Success(new MessageId(message.Id));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RequestException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception error) when (IsExpected(error))
        {
            logger?.LogWarning(error, "Ошибка отправки Telegram-сообщения.");
            return new TelegramSendResult.Failure(errorMapper.Map(error));
        }
        catch (ValueObjectValidationException error)
        {
            return new TelegramSendResult.Failure(new AppError(error.Code, error.Message));
        }
    }

    public async Task<TelegramDeleteResult> DeleteMessageAsync(ChatId chatId, MessageId messageId, CancellationToken cancellationToken)
    {
        try
        {
            await bot.DeleteMessage(chatId.Value, messageId.Value, cancellationToken);
            return new TelegramDeleteResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RequestException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception error) when (IsExpected(error))
        {
            logger?.LogWarning("Ошибка удаления Telegram-сообщения.");
            return new TelegramDeleteResult.Failure(errorMapper.Map(error));
        }
    }

    public async Task<TelegramSendResult> SendPhotoAsync(ChatId chatId, Stream photo, CancellationToken cancellationToken,
        TelegramCaption? caption = null, TelegramReplyKeyboard? keyboard = null)
    {
        try
        {
            var message = await bot.SendPhoto(chatId.Value, InputFile.FromStream(photo, "nerdy-ai-welcome.png"),
                caption: caption?.Value, replyMarkup: CreateKeyboard(keyboard), cancellationToken: cancellationToken);
            return new TelegramSendResult.Success(new MessageId(message.Id));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RequestException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception error) when (IsExpected(error))
        {
            logger?.LogWarning("Ошибка отправки приветственного изображения.");
            return new TelegramSendResult.Failure(errorMapper.Map(error));
        }
        catch (ValueObjectValidationException error)
        {
            return new TelegramSendResult.Failure(new AppError(error.Code, error.Message));
        }
    }

    public void Dispose()
    {
        http.Dispose();
    }

    private ReplyKeyboardMarkup? CreateKeyboard(TelegramReplyKeyboard? keyboard)
    {
        if (keyboard is null)
        {
            return null;
        }

        return new ReplyKeyboardMarkup(keyboard.Rows.Select(row => row.Select(label => new KeyboardButton(label.Value))))
        {
            ResizeKeyboard = true,
            IsPersistent = true,
            OneTimeKeyboard = false,
        };
    }

    private IncomingUpdate MapUpdate(Update update)
    {
        if (update.Message is not null && update.Message.Chat.Type != ChatType.Private)
        {
            return new IncomingUpdate(new UpdateId(update.Id), null, null);
        }

        var chatId = update.Message is null ? null : new ChatId(update.Message.Chat.Id);
        var text = update.Message?.Text is null ? null : new TelegramText(update.Message.Text);
        return new IncomingUpdate(new UpdateId(update.Id), chatId, text);
    }

    private bool IsExpected(Exception error)
    {
        return error is RequestException or HttpRequestException or OperationCanceledException or TimeoutException or JsonException;
    }
}
