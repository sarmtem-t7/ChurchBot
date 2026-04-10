namespace ChurchBot.Models;

public class Question
{
    public int Id { get; set; }
    public long UserChatId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Answer { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsAnswered { get; set; }
}
