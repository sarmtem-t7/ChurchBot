using ChurchBot;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.local.json", optional: true) // для локальной разработки
    .AddEnvironmentVariables();

builder.Services.Configure<BotConfig>(builder.Configuration);
builder.Services.AddSingleton<UserStateStorage>();
builder.Services.AddSingleton<PersistentStorage>(sp =>
{
    var config = sp.GetRequiredService<IOptions<BotConfig>>().Value;
    var logger = sp.GetRequiredService<ILogger<PersistentStorage>>();
    return new PersistentStorage(logger, config.DatabasePath);
});
builder.Services.AddSingleton<BotHandler>();
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var config = sp.GetRequiredService<IOptions<BotConfig>>().Value;
    return new TelegramBotClient(config.BotToken);
});
builder.Services.AddHostedService<BotService>();
builder.Services.AddControllers();

var app = builder.Build();

// Webhook endpoint — Telegram шлёт сюда обновления
app.MapPost("/webhook", async (
    HttpRequest request,
    Update update,
    BotHandler handler,
    ITelegramBotClient bot,
    IOptions<BotConfig> config,
    CancellationToken ct) =>
{
    // Проверяем секретный токен (X-Telegram-Bot-Api-Secret-Token)
    var secretToken = config.Value.WebhookSecretToken;
    if (!string.IsNullOrEmpty(secretToken))
    {
        if (!request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var receivedToken)
            || receivedToken != secretToken)
        {
            return Results.Unauthorized();
        }
    }

    await handler.HandleUpdateAsync(bot, update, ct);
    return Results.Ok();
});

app.MapGet("/", () => "ChurchBot is running");

app.Run();
