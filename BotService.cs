using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace ChurchBot;

public class BotService : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly BotHandler _handler;
    private readonly BotConfig _config;
    private readonly ILogger<BotService> _logger;

    public BotService(
        ITelegramBotClient bot,
        BotHandler handler,
        IOptions<BotConfig> config,
        ILogger<BotService> logger)
    {
        _bot = bot;
        _handler = handler;
        _config = config.Value;
        _logger = logger;
        _handler.SetClient(_bot, _config.GroupChatId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _bot.DeleteWebhook(cancellationToken: stoppingToken);

        var webhookUrl = _config.WebhookUrl;

        if (!string.IsNullOrEmpty(webhookUrl))
        {
            var secretToken = string.IsNullOrEmpty(_config.WebhookSecretToken)
                ? null
                : _config.WebhookSecretToken;

            await _bot.SetWebhook(
                url: $"{webhookUrl.TrimEnd('/')}/webhook",
                secretToken: secretToken,
                cancellationToken: stoppingToken
            );

            var me = await _bot.GetMe(stoppingToken);
            _logger.LogInformation("Bot @{Username} started in webhook mode: {Url}/webhook", me.Username, webhookUrl);
        }
        else
        {
            _logger.LogWarning("WebhookUrl not set — starting polling (local development only)");

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
            _logger.LogInformation("Bot @{Username} started (polling)", me.Username);
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}
