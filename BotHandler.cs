using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace ChurchBot;

public class BotHandler
{
    private ITelegramBotClient _bot = null!;
    private long _groupChatId;
    private readonly UserStateStorage _storage;

    private static readonly string[] Categories =
    [
        "🍕 Пицца с пастором",
        "❓ Вопрос пастору",
        "🙋 Вопрос молодёжному лидеру",
        "💡 Идея для молодёжки",
        "🙏 Молитвенная нужда"
    ];

    public BotHandler(UserStateStorage storage)
    {
        _storage = storage;
    }

    public void SetClient(ITelegramBotClient bot, long groupChatId)
    {
        _bot = bot;
        _groupChatId = groupChatId;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message) return;
        if (message.Text is not { } text) return;

        // Защита от дублей: пропускаем уже обработанные сообщения
        if (!_storage.TryMarkProcessed(message.MessageId)) return;

        var chatId = message.Chat.Id;
        bool isGroup = chatId == _groupChatId;

        // Ответ из группового чата → переслать пользователю
        if (isGroup && message.ReplyToMessage is { } reply)
        {
            var userId = _storage.GetUserByMessage(reply.MessageId);
            if (userId is not null)
            {
                await _bot.SendMessage(
                    chatId: userId.Value,
                    text: $"📩 *Ответ от команды:*\n\n{EscapeMarkdown(text)}",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                    cancellationToken: ct
                );
            }
            return;
        }

        // Личный чат с пользователем
        if (isGroup) return;

        if (text == "/start")
        {
            _storage.ClearCategory(chatId);
            await _bot.SendMessage(
                chatId: chatId,
                text: "👋 Привет! Это анонимный бот нашей церкви.\n\nВыбери категорию обращения:",
                replyMarkup: BuildCategoryKeyboard(),
                cancellationToken: ct
            );
            return;
        }

        // Если текст совпадает с одной из категорий — сохраняем и просим ввести сообщение
        if (Categories.Contains(text))
        {
            // Пользователь уже выбрал категорию и ждёт ввода — не отправлять подсказку повторно
            if (_storage.GetCategory(chatId) is not null) return;

            // Сначала очищаем, потом устанавливаем (атомарный сброс состояния)
            _storage.ClearCategory(chatId);
            _storage.SetCategory(chatId, text);
            var hint = text switch
            {
                "🍕 Пицца с пастором" => "Отлично! Напиши о чём хотел бы поговорить с пастором за чашкой чая или пиццей. Это может быть личный вопрос, духовная тема или просто желание пообщаться 😊",
                "❓ Вопрос пастору" => "Напиши свой вопрос пастору. Это может быть вопрос о вере, Библии, жизненной ситуации или о чём-то что давно тебя беспокоит 🙏",
                "🙋 Вопрос молодёжному лидеру" => "Напиши свой вопрос молодёжному лидеру. Это может быть про служение, мероприятия, отношения в команде или личное 💬",
                "💡 Идея для молодёжки" => "Поделись своей идеей! Что можно улучшить, добавить или изменить на молодёжных встречах? Любая идея важна ✨",
                "🙏 Молитвенная нужда" => "Напиши о чём тебе нужна молитва. Это останется анонимным — команда помолится за тебя ❤️",
                _ => "Напиши своё сообщение — оно будет отправлено анонимно 👇"
            };
            await _bot.SendMessage(
                chatId: chatId,
                text: hint,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: ct
            );
            return;
        }

        // Если есть выбранная категория — отправляем сообщение в группу
        var category = _storage.GetCategory(chatId);
        if (category is not null)
        {
            _storage.ClearCategory(chatId);

            var forwarded = await _bot.SendMessage(
                chatId: _groupChatId,
                text: $"📬 *{EscapeMarkdown(category)}*\n\n{EscapeMarkdown(text)}",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                cancellationToken: ct
            );

            _storage.MapMessage(forwarded.MessageId, chatId);

            await _bot.SendMessage(
                chatId: chatId,
                text: "✅ Сообщение отправлено анонимно!\n\nЕсли хочешь написать ещё, выбери категорию:",
                replyMarkup: BuildCategoryKeyboard(),
                cancellationToken: ct
            );
            return;
        }

        // Нет выбранной категории — напоминаем
        await _bot.SendMessage(
            chatId: chatId,
            text: "Сначала выбери категорию 👇",
            replyMarkup: BuildCategoryKeyboard(),
            cancellationToken: ct
        );
    }

    public Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiEx => $"Telegram API Error [{apiEx.ErrorCode}]: {apiEx.Message}",
            _ => exception.ToString()
        };
        Console.Error.WriteLine(errorMessage);
        return Task.CompletedTask;
    }

    private static ReplyKeyboardMarkup BuildCategoryKeyboard()
    {
        return new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { Categories[0] },
            new KeyboardButton[] { Categories[1] },
            new KeyboardButton[] { Categories[2] },
            new KeyboardButton[] { Categories[3] },
            new KeyboardButton[] { Categories[4] },
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };
    }

    private static string EscapeMarkdown(string text)
    {
        // Экранируем специальные символы MarkdownV2
        return text
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
}
