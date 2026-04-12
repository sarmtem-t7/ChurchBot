namespace ChurchBot;

public class CategoryConfig
{
    public string Label { get; set; } = string.Empty;
    public string Hint { get; set; } = string.Empty;
}

public class BotConfig
{
    public string BotToken { get; set; } = string.Empty;
    public long GroupChatId { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecretToken { get; set; }
    public string DatabasePath { get; set; } = "churchbot.db";
    public List<CategoryConfig> Categories { get; set; } = [];
}
