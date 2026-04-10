using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace ChurchBot;

public class BotService : BackgroundService
{
    private readonly TelegramBotClient _bot;
    private readonly BotHandler _handler;

    public BotService(IOptions<BotConfig> config, BotHandler handler)
    {
        _bot = new TelegramBotClient(config.Value.BotToken);
        _handler = handler;
        _handler.SetClient(_bot, config.Value.GroupChatId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message]
        };

        _bot.StartReceiving(
            updateHandler: _handler.HandleUpdateAsync,
            errorHandler: _handler.HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken
        );

        var me = await _bot.GetMe(stoppingToken);
        Console.WriteLine($"Бот @{me.Username} запущен и слушает сообщения...");

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
