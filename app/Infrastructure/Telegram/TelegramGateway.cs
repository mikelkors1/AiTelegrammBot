using System.Net;
using System.Text.Json;
using ItmoBot.Application.Messaging;
using ItmoBot.Configuration.Models;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Exceptions;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Validation;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types;
using Telegram.Bot;

namespace ItmoBot.Infrastructure.Telegram;

public sealed class TelegramGateway : ITelegramGateway
{
    private readonly HttpClient http;
    private readonly ITelegramBotClient bot;
    private readonly ILogger? logger;
    public TelegramGateway(Settings settings, HttpClient httpClient, ILogger? logger = null)
    {
        this.logger = logger;
        http = httpClient;
        bot = new TelegramBotClient(new TelegramBotClientOptions(settings.BotToken.Value) { RetryThreshold = 0 }, http);
    }

    public async Task<Result<string>> InitializeAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var me = await bot.GetMe(timeout.Token);
            var webhook = await bot.GetWebhookInfo(timeout.Token);
            if (!string.IsNullOrEmpty(webhook.Url))
            {
                return new Result<string>.Failure(new AppError(
                    ErrorCode.WebhookConfigured,
                    "У бота установлен webhook. Удалите его перед запуском polling или используйте отдельного учебного бота."));
            }

            return new Result<string>.Success(TelegramChecks.BotUsername(me.Username ?? ""));
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
            logger?.LogWarning("Ошибка инициализации Telegram. Код: {Code}.", MapError(error).Code);
            return new Result<string>.Failure(MapError(error));
        }
        catch (InputValidationException error)
        {
            return new Result<string>.Failure(new AppError(error.Code, error.Message));
        }
    }

    public async Task<Result<IReadOnlyList<IncomingUpdate>>> GetUpdatesAsync(int offset, CancellationToken cancellationToken)
    {
        try
        {
            var updates = await bot.GetUpdates(offset: offset, timeout: 25,
                allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery], cancellationToken: cancellationToken);
            return new Result<IReadOnlyList<IncomingUpdate>>.Success(updates.Select(update =>
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
            logger?.LogWarning("Ошибка получения Telegram updates. Код: {Code}.", MapError(error).Code);
            return new Result<IReadOnlyList<IncomingUpdate>>.Failure(MapError(error));
        }
        catch (InputValidationException error)
        {
            return new Result<IReadOnlyList<IncomingUpdate>>.Failure(new AppError(error.Code, error.Message));
        }
    }

    public async Task<Result<int>> SendTextAsync(long chatId, string text, CancellationToken cancellationToken,
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
            var message = await bot.SendMessage(chatId, text, parseMode: parseMode,
                replyMarkup: markup, cancellationToken: cancellationToken);
            return new Result<int>.Success(TelegramChecks.MessageId(message.Id));
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
            logger?.LogWarning("Ошибка отправки Telegram-сообщения. Код: {Code}.", MapError(error).Code);
            return new Result<int>.Failure(MapError(error));
        }
        catch (InputValidationException error)
        {
            return new Result<int>.Failure(new AppError(error.Code, error.Message));
        }
    }

    public async Task<Result> DeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken)
    {
        try
        {
            await bot.DeleteMessage(chatId, messageId, cancellationToken);
            return new Result.Success();
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
            return new Result.Failure(MapError(error));
        }
    }

    public async Task<Result<int>> SendPhotoAsync(long chatId, Stream photo, CancellationToken cancellationToken,
        string? caption = null, TelegramReplyKeyboard? keyboard = null)
    {
        try
        {
            var message = await bot.SendPhoto(chatId, InputFile.FromStream(photo, "nerdy-ai-welcome.png"),
                caption: caption, replyMarkup: CreateKeyboard(keyboard), cancellationToken: cancellationToken);
            return new Result<int>.Success(TelegramChecks.MessageId(message.Id));
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
            return new Result<int>.Failure(MapError(error));
        }
        catch (InputValidationException error)
        {
            return new Result<int>.Failure(new AppError(error.Code, error.Message));
        }
    }

    public async Task<Result<int>> SendInlineTextAsync(long chatId, string text, TelegramInlineKeyboard keyboard, CancellationToken cancellationToken)
    {
        try
        {
            var markup = new InlineKeyboardMarkup(keyboard.Rows.Select(row => row.Select(button => InlineKeyboardButton.WithCallbackData(button.Label, button.Data))));
            var message = await bot.SendMessage(chatId, text, replyMarkup: markup, cancellationToken: cancellationToken);
            return new Result<int>.Success(TelegramChecks.MessageId(message.Id));
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
            return new Result<int>.Failure(MapError(error));
        }
    }

    public async Task<Result> AnswerCallbackAsync(string callbackId, CancellationToken cancellationToken)
    {
        try
        {
            await bot.AnswerCallbackQuery(callbackId, cancellationToken: cancellationToken);
            return new Result.Success();
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
            return new Result.Failure(MapError(error));
        }
    }

    public async Task<Result> RemoveInlineKeyboardAsync(long chatId, int messageId, CancellationToken cancellationToken)
    {
        try
        {
            await bot.EditMessageReplyMarkup(chatId, messageId, replyMarkup: null, cancellationToken: cancellationToken);
            return new Result.Success();
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
            return new Result.Failure(MapError(error));
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

        return new ReplyKeyboardMarkup(keyboard.Rows.Select(row => row.Select(label => new KeyboardButton(label))))
        {
            ResizeKeyboard = true,
            IsPersistent = true,
            OneTimeKeyboard = false,
        };
    }

    private IncomingUpdate MapUpdate(Update update)
    {
        if (update.CallbackQuery is { } callback)
        {
            var message = callback.Message;
            if (message?.Chat.Type != ChatType.Private || callback.From.Id <= 0)
            {
                return new IncomingUpdate(AgentChecks.UpdateId(update.Id), null, null);
            }

            var data = callback.Data ?? "";
            if (System.Text.Encoding.UTF8.GetByteCount(data) > 64)
            {
                data = "";
            }

            return new IncomingUpdate(AgentChecks.UpdateId(update.Id), AgentChecks.ChatId(message.Chat.Id), null,
                AgentChecks.OwnerId(callback.From.Id), new TelegramCallback(TelegramChecks.CallbackQueryId(callback.Id), TelegramChecks.CallbackData(data), TelegramChecks.MessageId(message.Id)));
        }

        if (update.Message is not null && update.Message.Chat.Type != ChatType.Private)
        {
            return new IncomingUpdate(AgentChecks.UpdateId(update.Id), null, null);
        }

        long? chatId = update.Message is null ? null : AgentChecks.ChatId(update.Message.Chat.Id);
        string? text = update.Message?.Text is null ? null : TelegramChecks.TelegramText(update.Message.Text);
        long? owner = update.Message?.From is { Id: > 0 } from ? AgentChecks.OwnerId(from.Id) : null;
        return new IncomingUpdate(AgentChecks.UpdateId(update.Id), chatId, text, owner);
    }

    private bool IsExpected(Exception error)
    {
        return error is RequestException or HttpRequestException or OperationCanceledException or TimeoutException or JsonException;
    }

    public static HttpClient CreateHttpClient(Settings settings)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = settings.TelegramProxy is not null
        };
        if (handler.UseProxy)
        {
            var uri = settings.TelegramProxy!.Value;
            var proxy = new WebProxy(new UriBuilder(uri) { UserName = "", Password = "" }.Uri);
            if (uri.UserInfo.Length > 0)
            {
                var parts = uri.UserInfo.Split(':', 2);
                proxy.Credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]), parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "");
            }

            handler.Proxy = proxy;
        }

        // Без обхода заданного прокси при сбое. Один клиент для всех Telegram-вызовов.
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(40)
        };
    }


    private static AppError MapError(Exception error)
    {
        if (error is RequestException and not ApiRequestException && error.InnerException is { } inner)
        {
            return MapError(inner);
        }

        return error switch
        {
            ApiRequestException { ErrorCode: 401 } => new AppError(ErrorCode.TelegramUnauthorized, "Telegram отклонил токен. Проверьте BOT_TOKEN."),
            ApiRequestException { ErrorCode: 403 } => new AppError(ErrorCode.TelegramForbidden, "Telegram запретил действие. Проверьте права бота и доступ к чату."),
            ApiRequestException { ErrorCode: 409 } => new AppError(ErrorCode.TelegramPollingConflict, "Telegram обнаружил другой polling-процесс. Остановите второй экземпляр бота."),
            ApiRequestException { ErrorCode: 429 } => new AppError(ErrorCode.TelegramRateLimited, "Превышен лимит запросов Telegram. Повторите позже.", IsRetryable: true),
            ApiRequestException { ErrorCode: >= 500 } => new AppError(ErrorCode.TelegramUnavailable, "Сервис Telegram временно недоступен. Повторите позже.", IsRetryable: true),
            ApiRequestException => new AppError(ErrorCode.TelegramRejectedRequest, "Telegram отклонил запрос. Проверьте параметры сообщения и права бота."),
            OperationCanceledException or TimeoutException => new AppError(ErrorCode.TelegramTimeout, "Истекло время ожидания Telegram. Проверьте сеть и прокси.", IsRetryable: true),
            JsonException => new AppError(ErrorCode.TelegramRejectedRequest, "Telegram вернул некорректный ответ."),
            _ => new AppError(ErrorCode.TelegramUnavailable, "Не удалось подключиться к Telegram. Проверьте сеть и прокси.", IsRetryable: true),
        };
    }
}
