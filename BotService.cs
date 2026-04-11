using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace ChurchBot;

public class BotService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly BotHandler _handler;
    private readonly BotConfig _config;

    public BotService(ITelegramBotClient bot, BotHandler handler, IOptions<BotConfig> config)
    {
        _bot = bot;
        _handler = handler;
        _config = config.Value;
        _handler.SetClient(_bot, _config.GroupChatId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Удаляем старый webhook/polling и ставим новый
        await _bot.DeleteWebhook(cancellationToken: stoppingToken);

        var webhookUrl = _config.WebhookUrl;

        if (!string.IsNullOrEmpty(webhookUrl))
        {
            await _bot.SetWebhook(
                url: $"{webhookUrl.TrimEnd('/')}/webhook",
                cancellationToken: stoppingToken
            );
            var me = await _bot.GetMe(stoppingToken);
            Console.WriteLine($"Бот @{me.Username} запущен в режиме webhook: {webhookUrl}/webhook");
        }
        else
        {
            // Локально — fallback на polling
            Console.WriteLine("WebhookUrl не задан — запускаю polling (только для локальной разработки)");
            var receiverOptions = new Telegram.Bot.Polling.ReceiverOptions
            {
                AllowedUpdates = [Telegram.Bot.Types.Enums.UpdateType.Message]
            };
            _bot.StartReceiving(
                updateHandler: _handler.HandleUpdateAsync,
                errorHandler: _handler.HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken
            );
            var me = await _bot.GetMe(stoppingToken);
            Console.WriteLine($"Бот @{me.Username} запущен (polling)");
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}
