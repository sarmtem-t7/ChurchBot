namespace ChurchBot;

public class BotConfig
{
    public string BotToken { get; set; } = string.Empty;
    public long GroupChatId { get; set; }
    public string? WebhookUrl { get; set; }
}
