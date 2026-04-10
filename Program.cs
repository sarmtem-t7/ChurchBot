using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ChurchBot;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<BotConfig>(context.Configuration);
        services.AddSingleton<UserStateStorage>();
        services.AddSingleton<BotHandler>();
        services.AddHostedService<BotService>();
    })
    .Build();

await host.RunAsync();
