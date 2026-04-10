namespace ChurchBot;

public class UserStateStorage
{
    // userId → выбранная категория
    private readonly Dictionary<long, string> _pendingCategories = new();
    // messageId в группе → userId отправителя
    private readonly Dictionary<int, long> _messageToUser = new();
    // уже обработанные messageId — защита от дублей при нескольких экземплярах
    private readonly HashSet<int> _processedMessageIds = new();

    public void SetCategory(long userId, string category)
    {
        lock (_pendingCategories)
            _pendingCategories[userId] = category;
    }

    public string? GetCategory(long userId)
    {
        lock (_pendingCategories)
            return _pendingCategories.TryGetValue(userId, out var c) ? c : null;
    }

    public void ClearCategory(long userId)
    {
        lock (_pendingCategories)
            _pendingCategories.Remove(userId);
    }

    public void MapMessage(int groupMessageId, long userId)
    {
        lock (_messageToUser)
            _messageToUser[groupMessageId] = userId;
    }

    public long? GetUserByMessage(int groupMessageId)
    {
        lock (_messageToUser)
            return _messageToUser.TryGetValue(groupMessageId, out var u) ? u : null;
    }

    /// <summary>
    /// Возвращает true если messageId новый и был помечен как обработанный.
    /// Возвращает false если уже обрабатывался — значит дубль, нужно пропустить.
    /// </summary>
    public bool TryMarkProcessed(int messageId)
    {
        lock (_processedMessageIds)
            return _processedMessageIds.Add(messageId);
    }
}
