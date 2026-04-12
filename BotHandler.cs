using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace ChurchBot;

public class BotHandler
{
    private ITelegramBotClient _bot = null!;
    private long _groupChatId;
    private readonly UserStateStorage _stateStorage;
    private readonly PersistentStorage _persistentStorage;
    private readonly BotConfig _config;
    private readonly ILogger<BotHandler> _logger;

    private const string CancelLabel = "❌ Отмена";

    public BotHandler(
        UserStateStorage stateStorage,
        PersistentStorage persistentStorage,
        IOptions<BotConfig> config,
        ILogger<BotHandler> logger)
    {
        _stateStorage = stateStorage;
        _persistentStorage = persistentStorage;
        _config = config.Value;
        _logger = logger;
    }

    public void SetClient(ITelegramBotClient bot, long groupChatId)
    {
        _bot = bot;
        _groupChatId = groupChatId;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient _, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message) return;

        try
        {
            // Защита от дублей
            if (!_persistentStorage.TryMarkProcessed(message.MessageId)) return;

            var chatId = message.Chat.Id;
            bool isGroup = chatId == _groupChatId;

            if (isGroup)
            {
                await HandleGroupMessageAsync(message, ct);
                return;
            }

            await HandlePrivateMessageAsync(message, chatId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing message {MessageId}", message.MessageId);
        }
    }

    // ── Групповой чат ────────────────────────────────────────────────────────

    private async Task HandleGroupMessageAsync(Message message, CancellationToken ct)
    {
        var text = message.Text;

        // /stats — статистика обращений
        if (text is not null && text.StartsWith("/stats"))
        {
            await SendStatsAsync(message.Chat.Id, ct);
            return;
        }

        // Ответ на сообщение бота → переслать автору обращения
        if (message.ReplyToMessage is not { } reply) return;
        if (text is null) return;

        var userId = _persistentStorage.GetUserByMessage(reply.MessageId);
        if (userId is null)
        {
            _logger.LogWarning("Reply to group message {MessageId}: no user mapping found", reply.MessageId);
            return;
        }

        await _bot.SendMessage(
            chatId: userId.Value,
            text: $"📩 *Ответ от команды:*\n\n{EscapeMarkdown(text)}",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            cancellationToken: ct
        );
        _logger.LogInformation("Forwarded reply to user {UserId}", userId.Value);
    }

    // ── Личный чат ───────────────────────────────────────────────────────────

    private async Task HandlePrivateMessageAsync(Message message, long chatId, CancellationToken ct)
    {
        // Нетекстовые сообщения (фото, голосовые, стикеры и т.д.)
        if (message.Text is not { } text)
        {
            var markup = _stateStorage.GetCategory(chatId) is not null
                ? (IReplyMarkup)BuildCancelKeyboard()
                : BuildCategoryKeyboard();
            await _bot.SendMessage(
                chatId: chatId,
                text: "Пожалуйста, отправь текстовое сообщение 📝",
                replyMarkup: markup,
                cancellationToken: ct
            );
            return;
        }

        // /start — полный сброс состояния
        if (text == "/start")
        {
            _stateStorage.ClearCategory(chatId);
            await _bot.SendMessage(
                chatId: chatId,
                text: "👋 Привет! Это анонимный бот нашей церкви.\n\nВыбери категорию обращения:",
                replyMarkup: BuildCategoryKeyboard(),
                cancellationToken: ct
            );
            return;
        }

        // Отмена ввода
        if (text is "/cancel" or CancelLabel)
        {
            _stateStorage.ClearCategory(chatId);
            await _bot.SendMessage(
                chatId: chatId,
                text: "Отменено. Выбери категорию:",
                replyMarkup: BuildCategoryKeyboard(),
                cancellationToken: ct
            );
            return;
        }

        // Выбор категории
        var matchedCategory = _config.Categories.FirstOrDefault(c => c.Label == text);
        if (matchedCategory is not null)
        {
            // Уже ждём ввода — игнорируем повторное нажатие
            if (_stateStorage.GetCategory(chatId) is not null) return;

            _stateStorage.SetCategory(chatId, matchedCategory.Label);
            await _bot.SendMessage(
                chatId: chatId,
                text: matchedCategory.Hint,
                replyMarkup: BuildCancelKeyboard(),
                cancellationToken: ct
            );
            return;
        }

        // Отправка анонимного сообщения в группу
        var category = _stateStorage.GetCategory(chatId);
        if (category is not null)
        {
            _stateStorage.ClearCategory(chatId);

            var forwarded = await _bot.SendMessage(
                chatId: _groupChatId,
                text: $"📬 *{EscapeMarkdown(category)}*\n\n{EscapeMarkdown(text)}",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                cancellationToken: ct
            );

            _persistentStorage.MapMessage(forwarded.MessageId, chatId, category);
            _persistentStorage.IncrementStat(category);
            _logger.LogInformation("Message from {ChatId} in category '{Category}' forwarded to group", chatId, category);

            await _bot.SendMessage(
                chatId: chatId,
                text: "✅ Сообщение отправлено анонимно!\n\nЕсли хочешь написать ещё, выбери категорию:",
                replyMarkup: BuildCategoryKeyboard(),
                cancellationToken: ct
            );
            return;
        }

        // Категория не выбрана — напоминаем
        await _bot.SendMessage(
            chatId: chatId,
            text: "Сначала выбери категорию 👇",
            replyMarkup: BuildCategoryKeyboard(),
            cancellationToken: ct
        );
    }

    // ── Статистика ───────────────────────────────────────────────────────────

    private async Task SendStatsAsync(long targetChatId, CancellationToken ct)
    {
        var stats = _persistentStorage.GetStats();
        if (stats.Count == 0)
        {
            await _bot.SendMessage(chatId: targetChatId, text: "📊 Статистика пока пуста.", cancellationToken: ct);
            return;
        }

        var total = stats.Values.Sum();
        var lines = stats.Select(kv => $"{EscapeMarkdown(kv.Key)}: *{kv.Value}*");
        var statsText = $"📊 *Статистика обращений:*\n\n{string.Join("\n", lines)}\n\n_Всего: {total}_";

        await _bot.SendMessage(
            chatId: targetChatId,
            text: statsText,
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            cancellationToken: ct
        );
    }

    // ── Обработка ошибок ─────────────────────────────────────────────────────

    public Task HandlePollingErrorAsync(ITelegramBotClient _, Exception exception, CancellationToken ct)
    {
        var msg = exception switch
        {
            ApiRequestException apiEx => $"Telegram API Error [{apiEx.ErrorCode}]: {apiEx.Message}",
            _ => exception.ToString()
        };
        _logger.LogError("Polling error: {Error}", msg);
        return Task.CompletedTask;
    }

    // ── Клавиатуры ───────────────────────────────────────────────────────────

    private ReplyKeyboardMarkup BuildCategoryKeyboard()
    {
        var rows = _config.Categories
            .Select(c => new KeyboardButton[] { c.Label })
            .ToArray();
        return new ReplyKeyboardMarkup(rows) { ResizeKeyboard = true, OneTimeKeyboard = true };
    }

    private static ReplyKeyboardMarkup BuildCancelKeyboard() =>
        new(new[] { new KeyboardButton[] { CancelLabel } }) { ResizeKeyboard = true };

    // ── Экранирование MarkdownV2 ─────────────────────────────────────────────

    private static string EscapeMarkdown(string text) =>
        text
            .Replace("\\", "\\\\")
            .Replace("_", "\\_")
            .Replace("*", "\\*")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("~", "\\~")
            .Replace("`", "\\`")
            .Replace(">", "\\>")
            .Replace("#", "\\#")
            .Replace("+", "\\+")
            .Replace("-", "\\-")
            .Replace("=", "\\=")
            .Replace("|", "\\|")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace(".", "\\.")
            .Replace("!", "\\!");
}
