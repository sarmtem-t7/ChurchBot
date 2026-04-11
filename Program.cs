using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ChurchBot;
using Telegram.Bot;
using Telegram.Bot.Types;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<BotConfig>(builder.Configuration);
builder.Services.AddSingleton<UserStateStorage>();
builder.Services.AddSingleton<BotHandler>();
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BotConfig>>().Value;
    return new TelegramBotClient(config.BotToken);
});
builder.Services.AddHostedService<BotService>();
builder.Services.AddControllers();

var app = builder.Build();

// Webhook endpoint — Telegram шлёт сюда обновления
app.MapPost("/webhook", async (Update update, BotHandler handler, CancellationToken ct) =>
{
    await handler.HandleUpdateAsync(null!, update, ct);
    return Results.Ok();
});

app.MapGet("/", () => "ChurchBot is running");

app.Run();
